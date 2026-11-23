using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class FLowFieldAuthoring : MonoBehaviour {
    [SerializeField] private Vector3Int flowFieldSize = new Vector3Int(25, 25, 25);
    [SerializeField] private LayerMask obstacleLayer; // Слой пола и стен!

    private EntityManager entityManager;

    private void Start() {
        World defaultWorld = World.DefaultGameObjectInjectionWorld;
        if (defaultWorld == null || !defaultWorld.IsCreated) return;

        entityManager = defaultWorld.EntityManager;
        Entity fieldEntity = entityManager.CreateEntity(typeof(FlowFieldData));

        int3 gridSize = new int3(flowFieldSize.x, flowFieldSize.y, flowFieldSize.z);
        float cellRadius = 0.5f;
        float cellDiameter = cellRadius * 2f;
        float3 origin = transform.position;

        entityManager.SetComponentData(fieldEntity, new FlowFieldData {
            GridSize = gridSize,
            CellRadius = cellRadius,
            Origin = origin,
            DestinationCell = new int3(2, 1, 2),
            NeedsUpdate = true,

            AgentCostPenalty = 1,
            UpdateInterval = 0.15f,
            Timer = 0f
        });

        int totalVoxels = gridSize.x * gridSize.y * gridSize.z;
        var buffer = entityManager.AddBuffer<FlowCell>(fieldEntity);
        buffer.ResizeUninitialized(totalVoxels);

        bool[] isSolid = new bool[totalVoxels];
        // Уменьшили до 0.45f, чтобы стена не раздувалась на соседние пустые воксели
        Vector3 halfExtents = Vector3.one * (cellRadius * 0.45f);

        // Проход 1: Сканируем коллайдеры пола и стен
        int y = 0;
        while (y != gridSize.y) {
            int z = 0;
            while (z != gridSize.z) {
                int x = 0;
                while (x != gridSize.x) {
                    int idx = GetIndex(x, y, z, gridSize);
                    Vector3 center = new Vector3(
                        origin.x + (x + 0.5f) * cellDiameter,
                        origin.y + (y + 0.5f) * cellDiameter,
                        origin.z + (z + 0.5f) * cellDiameter
                    );
                    isSolid[idx] = Physics.CheckBox(center, halfExtents, Quaternion.identity, obstacleLayer);
                    x++;
                }
                z++;
            }
            y++;
        }

        // Проход 2: Настраиваем проходимые поверхности (пол, стены и верхние ребра крыш)
        y = 0;
        while (y != gridSize.y) {
            int z = 0;
            while (z != gridSize.z) {
                int x = 0;
                while (x != gridSize.x) {
                    int idx = GetIndex(x, y, z, gridSize);
                    if (isSolid[idx]) {
                        buffer[idx] = new FlowCell { BaseCost = 255 };
                        x++;
                        continue;
                    }

                    float3 normal = float3.zero;
                    bool hasFloor = false;

                    // 1. Проверяем 6 прямых соседей (пол и стены)
                    if (y != 0) {
                        hasFloor = isSolid[GetIndex(x, y - 1, z, gridSize)];
                    }

                    if (hasFloor) {
                        normal += new float3(0, 1, 0);
                    }
                    if (x != 0) {
                        if (isSolid[GetIndex(x - 1, y, z, gridSize)]) normal += new float3(1, 0, 0);
                    }
                    if (x != gridSize.x - 1) {
                        if (isSolid[GetIndex(x + 1, y, z, gridSize)]) normal += new float3(-1, 0, 0);
                    }
                    if (z != 0) {
                        if (isSolid[GetIndex(x, y, z - 1, gridSize)]) normal += new float3(0, 0, 1);
                    }
                    if (z != gridSize.z - 1) {
                        if (isSolid[GetIndex(x, y, z + 1, gridSize)]) normal += new float3(0, 0, -1);
                    }

                    // 2. Проверяем верхнее ребро стены (диагональ вниз-вбок),
                    // чтобы соединить вертикальную стену с горизонтальной крышей
                    if (math.lengthsq(normal) == 0f && y != 0) {
                        if (x != 0 && isSolid[GetIndex(x - 1, y - 1, z, gridSize)]) normal += new float3(1, 1, 0);
                        if (x != gridSize.x - 1 && isSolid[GetIndex(x + 1, y - 1, z, gridSize)]) normal += new float3(-1, 1, 0);
                        if (z != 0 && isSolid[GetIndex(x, y - 1, z - 1, gridSize)]) normal += new float3(0, 1, 1);
                        if (z != gridSize.z - 1 && isSolid[GetIndex(x, y - 1, z + 1, gridSize)]) normal += new float3(0, 1, -1);
                    }

                    bool hasSurface = math.lengthsq(normal) != 0f;
                    if (hasSurface) {
                        byte cost = 1;
                        buffer[idx] = new FlowCell {
                            BaseCost = cost,
                            SurfaceNormal = math.normalizesafe(normal)
                        };
                    } else {
                        buffer[idx] = new FlowCell { BaseCost = 255 };
                    }

                    x++;
                }
                z++;
            }
            y++;
        }
    }

    private int GetIndex(int x, int y, int z, int3 gridSize) {
        return (y * gridSize.x * gridSize.z) + (z * gridSize.x) + x;
    }
}
