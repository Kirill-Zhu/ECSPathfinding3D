using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
public partial struct FlowFieldSystem : ISystem {
    private EntityQuery _agentQuery;

    public void OnCreate(ref SystemState state) {
        _agentQuery = SystemAPI.QueryBuilder()
            .WithAll<FlowAgent, LocalTransform>()
            .Build();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state) {
        float dt = SystemAPI.Time.DeltaTime;

        foreach (var (fieldData, cellBuffer) in
                 SystemAPI.Query<RefRW<FlowFieldData>, DynamicBuffer<FlowCell>>()) {
            fieldData.ValueRW.Timer += dt;
            bool timerTriggered = fieldData.ValueRO.Timer >= fieldData.ValueRO.UpdateInterval;

            if (!fieldData.ValueRO.NeedsUpdate && !timerTriggered)
                continue;

            fieldData.ValueRW.Timer = 0f;
            fieldData.ValueRW.NeedsUpdate = false;

            NativeArray<LocalTransform> agentTransforms =
                _agentQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);

            var job = new CalculateFlowFieldJob {
                FieldData = fieldData.ValueRO,
                Cells = cellBuffer.AsNativeArray(),
                AgentTransforms = agentTransforms
            };

            state.Dependency = job.Schedule(state.Dependency);
        }
    }

    [BurstCompile]
    private struct CalculateFlowFieldJob : IJob {
        public FlowFieldData FieldData;
        public NativeArray<FlowCell> Cells;

        [DeallocateOnJobCompletion]
        [ReadOnly] public NativeArray<LocalTransform> AgentTransforms;

        public void Execute() {
            int totalCells = FieldData.GridSize.x * FieldData.GridSize.y * FieldData.GridSize.z;

            // 1. Сброс предыдущих расчетов и динамических штрафов
            for (int i = 0; i < totalCells; i++) {
                var cell = Cells[i];
                cell.BestCost = ushort.MaxValue;
                cell.BestDirection = float3.zero;
                cell.AgentPenalty = 0;
                Cells[i] = cell;
            }

            // 2. Начисляем штраф за каждого агента в его текущем 3D-вокселе
            float cellDiameter = FieldData.CellRadius * 2f;
            for (int i = 0; i < AgentTransforms.Length; i++) {
                float3 localPos = AgentTransforms[i].Position - FieldData.Origin;
                int x = (int)math.floor(localPos.x / cellDiameter);
                int y = (int)math.floor(localPos.y / cellDiameter);
                int z = (int)math.floor(localPos.z / cellDiameter);

                if (IsInsideGrid(x, y, z)) {
                    int idx = GetIndex(x, y, z);
                    var cell = Cells[idx];

                    // Начисляем штраф ТОЛЬКО на полу (BaseCost == 1), игнорируя стены (BaseCost == 3)
                    if (cell.BaseCost == 1) {
                        int newPenalty = cell.AgentPenalty + FieldData.AgentCostPenalty;
                        cell.AgentPenalty = (byte)math.min(newPenalty, 254 - cell.BaseCost);
                        Cells[idx] = cell;
                    }
                }
            }


            // 3. Инициализация 3D-цели
            if (!IsInsideGrid(FieldData.DestinationCell.x, FieldData.DestinationCell.y, FieldData.DestinationCell.z))
                return;

            int destIndex = GetIndex(FieldData.DestinationCell.x, FieldData.DestinationCell.y, FieldData.DestinationCell.z);
            var destCell = Cells[destIndex];
            destCell.BestCost = 0;
            Cells[destIndex] = destCell;

            // 4. 3D Интеграционное поле (Дейкстра по 6 граням вокселя: ±X, ±Y, ±Z)
            var openList = new NativeList<int3>(Allocator.Temp);
            openList.Add(FieldData.DestinationCell);

            while (openList.Length != 0) {
                int bestListIdx = 0;
                ushort lowestCost = ushort.MaxValue;
                int k = 0;
                while (k != openList.Length) {
                    int3 c = openList[k];
                    int idx = GetIndex(c.x, c.y, c.z);
                    if (Cells[idx].BestCost < lowestCost) {
                        lowestCost = Cells[idx].BestCost;
                        bestListIdx = k;
                    }
                    k++;
                }

                int3 currentCoord = openList[bestListIdx];
                openList.RemoveAtSwapBack(bestListIdx);
                int currentIdx = GetIndex(currentCoord.x, currentCoord.y, currentCoord.z);
                ushort currentBestCost = Cells[currentIdx].BestCost;

                // Проверяем 6 прямых граней и 12 диагональных ребер (чтобы волна переходила с пола на стены)
                int dy = -1;
                while (dy <= 1) {
                    int dz = -1;
                    while (dz <= 1) {
                        int dx = -1;
                        while (dx <= 1) {
                            int manhattan = math.abs(dx) + math.abs(dy) + math.abs(dz);
                            if (manhattan == 1 || manhattan == 2) {
                                bool blockedDiagonal = false;
                                if (manhattan == 2) {
                                    int adj1X = currentCoord.x + dx;
                                    int adj1Y = currentCoord.y;
                                    int adj1Z = currentCoord.z;

                                    int adj2X = currentCoord.x;
                                    int adj2Y = currentCoord.y + dy;
                                    int adj2Z = currentCoord.z + dz;

                                    if (dx != 0 && dy != 0) {
                                        adj2Y = currentCoord.y + dy;
                                        adj2Z = currentCoord.z;
                                    } else if (dx != 0 && dz != 0) {
                                        adj2Y = currentCoord.y;
                                        adj2Z = currentCoord.z + dz;
                                    } else if (dy != 0 && dz != 0) {
                                        adj1X = currentCoord.x;
                                        adj1Y = currentCoord.y + dy;
                                        adj2Y = currentCoord.y;
                                        adj2Z = currentCoord.z + dz;
                                    }

                                    bool solid1 = IsInsideGrid(adj1X, adj1Y, adj1Z) && Cells[GetIndex(adj1X, adj1Y, adj1Z)].BaseCost == 255;
                                    bool solid2 = IsInsideGrid(adj2X, adj2Y, adj2Z) && Cells[GetIndex(adj2X, adj2Y, adj2Z)].BaseCost == 255;

                                    if (solid1 && solid2) {
                                        blockedDiagonal = true;
                                    }
                                }

                                if (!blockedDiagonal) {
                                    CheckNeighbor(currentCoord + new int3(dx, dy, dz), currentBestCost, ref openList);
                                }
                            }
                            dx++;
                        }
                        dz++;
                    }
                    dy++;
                }
            }

            openList.Dispose();


            // 5. Построение 3D-векторов направления (для перелезания через края стен вверх и вниз)
            for (int y = 0; y < FieldData.GridSize.y; y++) {
                for (int z = 0; z < FieldData.GridSize.z; z++) {
                    for (int x = 0; x < FieldData.GridSize.x; x++) {
                        int currentIdx = GetIndex(x, y, z);
                        var currentCell = Cells[currentIdx];

                        ushort bestNeighborCost = ushort.MaxValue;
                        int3 bestDir = int3.zero;

                        for (int dy = -1; dy <= 1; dy++) {
                            for (int dz = -1; dz <= 1; dz++) {
                                for (int dx = -1; dx <= 1; dx++) {
                                    if (dx == 0 && dy == 0 && dz == 0) continue;

                                    int nx = x + dx;
                                    int ny = y + dy;
                                    int nz = z + dz;

                                    if (!IsInsideGrid(nx, ny, nz))
                                        continue;

                                    int neighborIdx = GetIndex(nx, ny, nz);
                                    if (Cells[neighborIdx].BaseCost == 255)
                                        continue;

                                    // Разрешаем диагональный переход через ребро крыши (manhattan == 2),
                                    // если хотя бы одна из смежных клеток открыта (не стена)
                                    if (currentCell.BaseCost != 255) {
                                        int manhattan = math.abs(dx) + math.abs(dy) + math.abs(dz);
                                        if (manhattan == 3) continue; // Не срезаем дальние 3D-углы

                                        if (manhattan == 2) {
                                            int blockedCount = 0;
                                            if (dx != 0 && Cells[GetIndex(x + dx, y, z)].BaseCost == 255) blockedCount++;
                                            if (dy != 0 && Cells[GetIndex(x, y + dy, z)].BaseCost == 255) blockedCount++;
                                            if (dz != 0 && Cells[GetIndex(x, y, z + dz)].BaseCost == 255) blockedCount++;

                                            if (blockedCount >= 2) continue;
                                        }
                                    }

                                    if (Cells[neighborIdx].BestCost < bestNeighborCost) {
                                        bestNeighborCost = Cells[neighborIdx].BestCost;
                                        bestDir = new int3(dx, dy, dz);
                                    }
                                }
                            }
                        }

                        currentCell.BestDirection = math.normalizesafe(new float3(bestDir.x, bestDir.y, bestDir.z));
                        Cells[currentIdx] = currentCell;
                    }
                }
            }

        }

        private void CheckNeighbor(int3 coord, ushort currentCost, ref NativeList<int3> openList) {
            if (!IsInsideGrid(coord.x, coord.y, coord.z))
                return;

            int idx = GetIndex(coord.x, coord.y, coord.z);
            var neighbor = Cells[idx];

            if (neighbor.BaseCost == 255) return; // Стена или пустой воздух

            int totalCellCost = neighbor.BaseCost + neighbor.AgentPenalty;
            int newCost = currentCost + totalCellCost;

            if (newCost < neighbor.BestCost) {
                neighbor.BestCost = (ushort)newCost;
                Cells[idx] = neighbor;
                openList.Add(coord);
            }
        }

        private bool IsInsideGrid(int x, int y, int z) {
            return x >= 0 && x < FieldData.GridSize.x &&
                   y >= 0 && y < FieldData.GridSize.y &&
                   z >= 0 && z < FieldData.GridSize.z;
        }

        private int GetIndex(int x, int y, int z) {
            return (y * FieldData.GridSize.x * FieldData.GridSize.z) + (z * FieldData.GridSize.x) + x;
        }
    }
}
