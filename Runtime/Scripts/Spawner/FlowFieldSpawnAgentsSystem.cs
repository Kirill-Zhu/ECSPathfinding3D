using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

partial struct FlowFieldSpawnAgentsSystem : ISystem {
    [BurstCompile]
    public void OnCreate(ref SystemState state) {
        state.RequireForUpdate<SpawnerComponent>();
        state.RequireForUpdate<SpawnRequest>();

    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state) {
        var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        EntityCommandBuffer ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        SpawnerComponent spawner = SystemAPI.GetSingleton<SpawnerComponent>();

        foreach (var (spawnComponent, spawnRequestEntity) in SystemAPI.Query<SpawnRequest>().WithEntityAccess()) {
            for (int i = 0; i < spawnComponent.spawnQuantity; i++) {
                Entity entity = ecb.Instantiate(spawner.AgentPrefab);
                ecb.SetComponent(entity, LocalTransform.FromPosition(new Unity.Mathematics.float3(5, 2, 5 + i)));
            }

            ecb.DestroyEntity(spawnRequestEntity);
        }


    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) {

    }
}
