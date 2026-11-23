using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class FlowFieldDebugVisualizer : MonoBehaviour {
    [SerializeField] private bool _showGrid = true;
    [SerializeField] private bool _showDirections = true;

    private EntityManager _entityManager;
    private EntityQuery _fieldQuery;

    private void Start() {
        World defaultWorld = World.DefaultGameObjectInjectionWorld;
        if (defaultWorld != null && defaultWorld.IsCreated) {
            _entityManager = defaultWorld.EntityManager;
            _fieldQuery = _entityManager.CreateEntityQuery(typeof(FlowFieldData), typeof(FlowCell));
        }
    }

    private void OnDrawGizmos() {
        if (!_showGrid || !Application.isPlaying) return;
        if (_entityManager == default || !_fieldQuery.HasSingleton<FlowFieldData>()) return;

        // Завершаем фоновые задачи перед чтением буфера для отрисовки
        _entityManager.CompleteAllTrackedJobs();

        Entity fieldEntity = _fieldQuery.GetSingletonEntity();
        FlowFieldData fieldData = _entityManager.GetComponentData<FlowFieldData>(fieldEntity);
        var cellBuffer = _entityManager.GetBuffer<FlowCell>(fieldEntity);

        int expectedTotal = fieldData.GridSize.x * fieldData.GridSize.y * fieldData.GridSize.z;
        if (cellBuffer.Length != expectedTotal) return;

        float cellDiameter = fieldData.CellRadius * 2f;
        Vector3 cubeSize = Vector3.one * (cellDiameter * 0.85f);

        int y = 0;
        while (y != fieldData.GridSize.y) {
            int z = 0;
            while (z != fieldData.GridSize.z) {
                int x = 0;
                while (x != fieldData.GridSize.x) {
                    int idx = (y * fieldData.GridSize.x * fieldData.GridSize.z) + (z * fieldData.GridSize.x) + x;
                    FlowCell cell = cellBuffer[idx];

                    if (cell.BaseCost == 255) {
                        x++;
                        continue;
                    }

                    Vector3 center = new Vector3(
                        fieldData.Origin.x + (x + 0.5f) * cellDiameter,
                        fieldData.Origin.y + (y + 0.5f) * cellDiameter,
                        fieldData.Origin.z + (z + 0.5f) * cellDiameter
                    );

                    if (x == fieldData.DestinationCell.x &&
                        y == fieldData.DestinationCell.y &&
                        z == fieldData.DestinationCell.z) {
                        Gizmos.color = Color.red;
                        Gizmos.DrawCube(center, cubeSize);
                    } else {
                        Gizmos.color = cell.BaseCost == 1
                            ? new Color(0f, 1f, 0f, 0.25f)
                            : new Color(1f, 0.8f, 0f, 0.35f);
                        Gizmos.DrawWireCube(center, cubeSize);
                    }

                    if (_showDirections && math.lengthsq(cell.BestDirection) != 0f) {
                        Gizmos.color = Color.cyan;
                        Vector3 dir = new Vector3(cell.BestDirection.x, cell.BestDirection.y, cell.BestDirection.z);
                        Gizmos.DrawRay(center, dir * (fieldData.CellRadius * 0.8f));
                    }

                    x++;
                }
                z++;
            }
            y++;
        }
    }
}