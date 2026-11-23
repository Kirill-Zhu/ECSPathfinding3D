using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;

[BurstCompile]
[UpdateAfter(typeof(FlowFieldSystem))]
[UpdateBefore(typeof(PhysicsSystemGroup))]
public partial struct FlowAgentPhysicsMovementSystem : ISystem {
    [BurstCompile]
    public void OnUpdate(ref SystemState state) {
        if (!SystemAPI.TryGetSingletonEntity<FlowFieldData>(out Entity fieldEntity))
            return;

        var fieldData = SystemAPI.GetComponent<FlowFieldData>(fieldEntity);
        var cellLookup = SystemAPI.GetBufferLookup<FlowCell>(true);
        var cellBuffer = cellLookup[fieldEntity];
        NativeArray<FlowCell> cellsArray = cellBuffer.AsNativeArray();

        float cellDiameter = fieldData.CellRadius * 2f;
        float3 targetWorldPos = fieldData.Origin + new float3(
            (fieldData.DestinationCell.x + 0.5f) * cellDiameter,
            0f,
            (fieldData.DestinationCell.y + 0.5f) * cellDiameter
        );

        // Создаем временный контейнер на 1 float для передачи результата между Job'ами
        var stopRadiusSqRef = new NativeReference<float>(Allocator.TempJob);

        // 1. Задача подсчета толпы вокруг цели и вычисления радиуса остановки
        var radiusJob = new CalculateCrowdRadiusJob {
            FieldData = fieldData,
            Cells = cellsArray,
            AgentDiameter = 1.0f, // Диаметр коллайдера вашего агента
            StopRadiusSqOut = stopRadiusSqRef
        };
        JobHandle radiusHandle = radiusJob.Schedule(state.Dependency);

        // 2. Задача движения агентов (зависит от radiusHandle)
        var moveJob = new MoveAgentsPhysicsJob {
            FieldData = fieldData,
            Cells = cellsArray,
            TargetWorldPos = targetWorldPos,
            StopRadiusSqRef = stopRadiusSqRef
        };
        JobHandle moveHandle = moveJob.ScheduleParallel(radiusHandle);

        // 3. Освобождаем память NativeReference строго после завершения moveJob
        state.Dependency = stopRadiusSqRef.Dispose(moveHandle);
    }

    [BurstCompile]
    private struct CalculateCrowdRadiusJob : IJob {
        [ReadOnly] public FlowFieldData FieldData;
        [ReadOnly] public NativeArray<FlowCell> Cells;
        public float AgentDiameter;
        public NativeReference<float> StopRadiusSqOut;

        public void Execute() {
            int crowdedAgents = 0;

            if (FieldData.AgentCostPenalty > 0) {
                for (int dy = -3; dy <= 3; dy++) {
                    for (int dx = -3; dx <= 3; dx++) {
                        int nx = FieldData.DestinationCell.x + dx;
                        int ny = FieldData.DestinationCell.y + dy;

                        if (nx >= 0 && nx < FieldData.GridSize.x && ny >= 0 && ny < FieldData.GridSize.y) {
                            int idx = ny * FieldData.GridSize.x + nx;
                            crowdedAgents += Cells[idx].AgentPenalty / FieldData.AgentCostPenalty;
                        }
                    }
                }
            }

            float baseStopRadius = FieldData.CellRadius * 1.5f;
            float crowdRadius = math.sqrt(crowdedAgents) * (AgentDiameter * 0.65f);
            float effectiveStopRadius = math.max(baseStopRadius, crowdRadius);

            // Записываем квадрат радиуса в NativeReference
            StopRadiusSqOut.Value = effectiveStopRadius * effectiveStopRadius;
        }
    }

    [BurstCompile]
    private partial struct MoveAgentsPhysicsJob : IJobEntity {
        [ReadOnly] public FlowFieldData FieldData;
        [ReadOnly] public NativeArray<FlowCell> Cells;
        public float3 TargetWorldPos;
        [ReadOnly] public NativeReference<float> StopRadiusSqRef;

        private void Execute(ref PhysicsVelocity velocity, ref PhysicsMass mass, in LocalTransform transform, in FlowAgent agent) {
            float cellDiameter = FieldData.CellRadius * 2f;

            // 1. Остановка возле цели
            if (math.distancesq(transform.Position, TargetWorldPos) <= StopRadiusSqRef.Value) {
                velocity.Linear = float3.zero;
                velocity.Angular = float3.zero;
                mass.InverseInertia = float3.zero;
                return;
            }

            // 2. Читаем строго текущий воксель агента (без Look-Ahead, чтобы не было колебаний на углах)
            int3 voxel = (int3)math.floor((transform.Position - FieldData.Origin) / cellDiameter);
            if (!IsInside(voxel)) return;

            FlowCell cell = Cells[GetIndex(voxel)];

            // Подхват вокселя сверху (y + 1), если центр сферы чуть задел слой крыши/пола
            if (cell.BaseCost == 255 && voxel.y + 1 < FieldData.GridSize.y) {
                int3 aboveVoxel = new int3(voxel.x, voxel.y + 1, voxel.z);
                FlowCell aboveCell = Cells[GetIndex(aboveVoxel)];
                if (aboveCell.BaseCost != 255) {
                    cell = aboveCell;
                }
            }

            // Если сфера в воздухе за краем стены — опускаем вниз и слегка к центру цели
            if (cell.BaseCost == 255) {
                float3 toTarget = math.normalizesafe(new float3(TargetWorldPos.x - transform.Position.x, 0f, TargetWorldPos.z - transform.Position.z));
                velocity.Linear = new float3(toTarget.x * 2f, -agent.MoveSpeed, toTarget.z * 2f);
                mass.InverseInertia = float3.zero;
                velocity.Angular = float3.zero;
                return;
            }

            float3 dir = cell.BestDirection;

            // Страховка на случай нулевого вектора в вокселе (чтобы агент никогда не замирал на месте)
            if (math.lengthsq(dir) < 0.01f) {
                dir = math.normalizesafe(TargetWorldPos - transform.Position);
            }

            // Делаем диагональный спуск пологим (больше вперед, меньше вниз)
            if (dir.y < -0.1f && (math.abs(dir.x) > 0.1f || math.abs(dir.z) > 0.1f)) {
                dir.y *= 0.45f;
                dir = math.normalizesafe(dir);
            }

            float3 moveVel = dir * agent.MoveSpeed;
            float3 stickForce = -cell.SurfaceNormal * 1.5f;

            velocity.Linear = moveVel + stickForce;
            mass.InverseInertia = float3.zero;
            velocity.Angular = float3.zero;
        }

        private bool IsInside(int3 v) {
            return v.x >= 0 && v.x < FieldData.GridSize.x &&
                   v.y >= 0 && v.y < FieldData.GridSize.y &&
                   v.z >= 0 && v.z < FieldData.GridSize.z;
        }

        private int GetIndex(int3 v) {
            return (v.y * FieldData.GridSize.x * FieldData.GridSize.z) + (v.z * FieldData.GridSize.x) + v.x;
        }
    }

}
