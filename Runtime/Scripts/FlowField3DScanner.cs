using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class FlowField3DScanner : MonoBehaviour {
    [Header("Настройки 3D-сетки")]
    public int3 GridSize = new int3(30, 15, 30);
    public float CellRadius = 0.5f;
    public LayerMask ObstacleLayer; // Включите сюда слои Пола и Стен!
    public Transform DestinationTransform;

    [Header("Настройки толпы")]
    public byte AgentCostPenalty = 5;
    public float UpdateInterval = 0.2f;

    private void Start() {
        EntityManager em = World.DefaultGameObjectInjectionWorld.EntityManager;
        Entity fieldEntity = em.CreateEntity(typeof(FlowFieldData));

        float cellDiameter = CellRadius * 2f;
        float3 origin = transform.position;

        // Вычисляем 3D-координаты целевой ячейки
        int3 destCell = int3.zero;
        if (DestinationTransform != null) {
            float3 localDest = (float3)DestinationTransform.position - origin;
            destCell = (int3)math.floor(localDest / cellDiameter);
        }

        em.SetComponentData(fieldEntity, new FlowFieldData {
            GridSize = GridSize,
            CellRadius = CellRadius,
            Origin = origin,
            DestinationCell = destCell,
            NeedsUpdate = true,
            AgentCostPenalty = AgentCostPenalty,
            UpdateInterval = UpdateInterval
        });

        int totalVoxels = GridSize.x * GridSize.y * GridSize.z;
        bool[] isSolid = new bool[totalVoxels];
        Vector3 halfExtents = Vector3.one * (CellRadius * 0.9f);

        // Проход 1: Находим все твердые блоки (стены и пол)
        DynamicBuffer<FlowCell> buffer = em.AddBuffer<FlowCell>(fieldEntity);
        buffer.ResizeUninitialized(totalVoxels);

        for (int y = 0; y < GridSize.y; y++) {
                for (int z = 0; z < GridSize.z; z++) {
                    for (int x = 0; x < GridSize.x; x++) {
                        int idx = GetIndex(x, y, z);
                        if (isSolid[idx]) {
                            buffer[idx] = new FlowCell { BaseCost = 255 };
                            continue;
                        }

                        float3 normal = float3.zero;
                        bool hasFloor = y > 0 && isSolid[GetIndex(x, y - 1, z)];

                        if (hasFloor) normal += new float3(0, 1, 0);
                        if (x > 0 && isSolid[GetIndex(x - 1, y, z)]) normal += new float3(1, 0, 0);
                        if (x < GridSize.x - 1 && isSolid[GetIndex(x + 1, y, z)]) normal += new float3(-1, 0, 0);
                        if (z > 0 && isSolid[GetIndex(x, y, z - 1)]) normal += new float3(0, 0, 1);
                        if (z < GridSize.z - 1 && isSolid[GetIndex(x, y, z + 1)]) normal += new float3(0, 0, -1);

                        if (math.lengthsq(normal) > 0.001f) {
                            buffer[idx] = new FlowCell {
                                BaseCost = hasFloor ? (byte)1 : (byte)3, // Пол = 1, Стена = 3
                                SurfaceNormal = math.normalizesafe(normal)
                            };
                        } else {
                            buffer[idx] = new FlowCell { BaseCost = 255 }; // Воздух (непроходим)
                        }
                    }
                }
            }
    }

    private int GetIndex(int x, int y, int z) {
        return (y * GridSize.x * GridSize.z) + (z * GridSize.x) + x;
    }
}
