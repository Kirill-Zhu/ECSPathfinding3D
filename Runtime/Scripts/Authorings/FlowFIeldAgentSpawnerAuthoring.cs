using Unity.Entities;
using UnityEngine;

public class FlowFIeldAgentSpawnerAuthoring : MonoBehaviour
{
    [SerializeField] GameObject agentPrefab;

    public class Baker : Baker<FlowFIeldAgentSpawnerAuthoring> {
        public override void Bake(FlowFIeldAgentSpawnerAuthoring authoring) {

            Entity entity = GetEntity(TransformUsageFlags.None);

            Entity prefabEntity = GetEntity(authoring.agentPrefab, TransformUsageFlags.Dynamic);

            AddComponent(entity, new SpawnerComponent {
                AgentPrefab = prefabEntity,
            });


        }
    }
}

public struct SpawnerComponent: IComponentData {
    public Entity AgentPrefab;
}

public struct SpawnRequest: IComponentData {
    public int spawnQuantity;
}