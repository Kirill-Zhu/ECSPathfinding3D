using Unity.Entities;
using UnityEngine;

class FLowAgentAuthoring : MonoBehaviour
{

    [SerializeField] float moveSpeed = 1;
    [SerializeField] float avoidanceRadius = 0.5f;
    [SerializeField] float avoiddanceForce = 3f;
    class FLowAgentAuthoringBaker : Baker<FLowAgentAuthoring> {
        public override void Bake(FLowAgentAuthoring authoring) {

            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new FlowAgent { 
                MoveSpeed = Random.Range(3f,5f),
                AvoidanceRadius = authoring.avoidanceRadius,
                AvoidanceForce = authoring.avoiddanceForce
            });
 
        }
    }
}


