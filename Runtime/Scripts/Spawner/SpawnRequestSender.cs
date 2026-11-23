using Unity.Entities;
using UnityEngine;

public class SpawnRequestSender : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
            SpawnFlowFieldAgentEntity();
    }

    public void SpawnFlowFieldAgentEntity() {
        Debug.Log("Spawn flow field agent request");
        World defaultWorld = World.DefaultGameObjectInjectionWorld;
        if (defaultWorld == null || !defaultWorld.IsCreated) return;

        EntityManager entityManager = defaultWorld.EntityManager;

        // Create a lightweight entity with the SpawnRequest tag
        Entity requestEntity = entityManager.CreateEntity();
        entityManager.AddComponentData(requestEntity, new SpawnRequest { spawnQuantity = 2 });
    }
}
