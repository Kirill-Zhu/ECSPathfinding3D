using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;

public class FlowFieldObstacleScanner : MonoBehaviour {
    [SerializeField] private LayerMask _obstacleLayer;

    private void Start() {

        ScanObstacles();
    }

    [ContextMenu("Scan for obstacles")]
    public void ScanObstacles() {
        EntityManager em = World.DefaultGameObjectInjectionWorld.EntityManager;
        EntityQuery query = em.CreateEntityQuery(typeof(FlowFieldData), typeof(FlowCell));

        if (!query.HasSingleton<FlowFieldData>()) return;

        Entity fieldEntity = query.GetSingletonEntity();
        FlowFieldData fieldData = em.GetComponentData<FlowFieldData>(fieldEntity);
        DynamicBuffer<FlowCell> cells = em.GetBuffer<FlowCell>(fieldEntity);

        float cellDiameter = fieldData.CellRadius * 2f;
        Vector3 halfExtents = Vector3.one * (fieldData.CellRadius * 0.9f); // Slightly smaller to avoid adjacent false positives

        for (int y = 0; y < fieldData.GridSize.y; y++) {
            for (int x = 0; x < fieldData.GridSize.x; x++) {
                int index = y * fieldData.GridSize.x + x;

                Vector3 cellCenter = new Vector3(
                    fieldData.Origin.x + (x * cellDiameter) + fieldData.CellRadius,
                    fieldData.Origin.y,
                    fieldData.Origin.z + (y * cellDiameter) + fieldData.CellRadius
                );

                // Check if any collider on the Obstacle layer overlaps this cell
                bool isBlocked = Physics.CheckBox(cellCenter, halfExtents, Quaternion.identity, _obstacleLayer);

                FlowCell cell = cells[index];
                cell.BaseCost = isBlocked ? (byte)255 : (byte)1;
                cells[index] = cell;
            }
        }

        // Trigger path recalculation
        fieldData.NeedsUpdate = true;
        em.SetComponentData(fieldEntity, fieldData);
    }
}
