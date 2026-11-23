using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class FlowFieldDestinationSetter : MonoBehaviour {
    private EntityManager _entityManager;
    private EntityQuery _fieldQuery;

    private void Start() {
        World defaultWorld = World.DefaultGameObjectInjectionWorld;
        if (defaultWorld != null && defaultWorld.IsCreated) {
            _entityManager = defaultWorld.EntityManager;
            _fieldQuery = _entityManager.CreateEntityQuery(typeof(FlowFieldData));
        }
    }

    private void Update() {
        if (Input.GetMouseButtonDown(0)) {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit)) {
                float3 safeWorldPos = (float3)(hit.point + hit.normal * 0.25f);
                SetDestinationFromWorldPos(safeWorldPos);
            }
        }
    #region TOUCH
    #if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        if (Input.touchCount > 0) {

            Ray ray =  Camera.main.ScreenPointToRay(Input.GetTouch(0).position);
            if (Physics.Raycast(ray, out RaycastHit hit)) {
                float3 safeWorldPos = (float3)(hit.point + hit.normal * 0.25f);
                SetDestinationFromWorldPos(safeWorldPos);
            }
        }
#endif
#endregion
    }

    public void SetDestinationFromWorldPos(float3 worldPosition) {
        if (_entityManager == default || !_fieldQuery.HasSingleton<FlowFieldData>()) return;

        // «авершаем фоновые задачи перед чтением/записью буфера из главного потока
        _entityManager.CompleteAllTrackedJobs();

        Entity fieldEntity = _fieldQuery.GetSingletonEntity();
        FlowFieldData fieldData = _entityManager.GetComponentData<FlowFieldData>(fieldEntity);
        var cellBuffer = _entityManager.GetBuffer<FlowCell>(fieldEntity);

        float cellDiameter = fieldData.CellRadius * 2f;
        float3 localPos = worldPosition - fieldData.Origin;

        int cellX = (int)math.floor(localPos.x / cellDiameter);
        int cellY = (int)math.floor(localPos.y / cellDiameter);
        int cellZ = (int)math.floor(localPos.z / cellDiameter);

        if (!IsInsideGrid(cellX, cellY, cellZ, fieldData.GridSize)) {
            Debug.LogWarning($"Click out of grid bounds! Cell: ({cellX}, {cellY}, {cellZ})");
            return;
        }

        int3 targetCell = new int3(cellX, cellY, cellZ);
        int targetIdx = GetIndex(targetCell.x, targetCell.y, targetCell.z, fieldData.GridSize);

        // ≈сли клик попал в непроходимый воксель (255), ищем ближайший проходимый вокруг него
        if (cellBuffer[targetIdx].BaseCost == 255) {
            if (!TryFindNearestWalkable(worldPosition, targetCell, fieldData, cellBuffer, out targetCell)) {
                Debug.LogWarning($"No walkable voxel found near ({cellX}, {cellY}, {cellZ})!");
                return;
            }
        }

        fieldData.DestinationCell = targetCell;
        fieldData.NeedsUpdate = true;
        _entityManager.SetComponentData(fieldEntity, fieldData);

        Debug.Log($"Destination updated to walkable cell: {fieldData.DestinationCell}");
    }

    private bool TryFindNearestWalkable(
        float3 hitWorldPos,
        int3 centerCell,
        FlowFieldData fieldData,
        DynamicBuffer<FlowCell> cellBuffer,
        out int3 bestCell) {
        bestCell = centerCell;
        float bestDistSq = float.MaxValue;
        bool found = false;
        float cellDiameter = fieldData.CellRadius * 2f;

        // ѕровер€ем куб 3x3x3 вокруг точки попадани€ и выбираем ближайший проходимый воксель
        int dy = -1;
        while (dy <= 1) {
            int dz = -1;
            while (dz <= 1) {
                int dx = -1;
                while (dx <= 1) {
                    int nx = centerCell.x + dx;
                    int ny = centerCell.y + dy;
                    int nz = centerCell.z + dz;

                    if (IsInsideGrid(nx, ny, nz, fieldData.GridSize)) {
                        int nIdx = GetIndex(nx, ny, nz, fieldData.GridSize);
                        if (cellBuffer[nIdx].BaseCost != 255) {
                            float3 voxelCenter = fieldData.Origin + new float3(
                                (nx + 0.5f) * cellDiameter,
                                (ny + 0.5f) * cellDiameter,
                                (nz + 0.5f) * cellDiameter
                            );

                            float distSq = math.distancesq(hitWorldPos, voxelCenter);
                            if (distSq < bestDistSq) {
                                bestDistSq = distSq;
                                bestCell = new int3(nx, ny, nz);
                                found = true;
                            }
                        }
                    }
                    dx++;
                }
                dz++;
            }
            dy++;
        }

        return found;
    }

    private bool IsInsideGrid(int x, int y, int z, int3 gridSize) {
        return x >= 0 && x < gridSize.x &&
               y >= 0 && y < gridSize.y &&
               z >= 0 && z < gridSize.z;
    }

    private int GetIndex(int x, int y, int z, int3 gridSize) {
        return (y * gridSize.x * gridSize.z) + (z * gridSize.x) + x;
    }
}
