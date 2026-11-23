using Unity.Entities;
using Unity.Mathematics;

public struct FlowFieldData : IComponentData {
    public int3 GridSize;
    public float CellRadius;
    public float3 Origin;
    public int3 DestinationCell; // Было int2 -> стало int3 (X, Y, Z)
    public bool NeedsUpdate;

    // Настройки динамической плотности агентов
    public byte AgentCostPenalty; // Сколько добавлять к стоимости за 1 агента (например, 5 или 10)
    public float UpdateInterval;  // Как часто обновлять поле из-за движения толпы (например, 0.15 сек)
    public float Timer;           // Внутренний таймер
}

[InternalBufferCapacity(0)]
public struct FlowCell : IBufferElementData {
    public byte BaseCost;        // Статичная стоимость ландшафта (1 = земля, 255 = стена)
    public byte AgentPenalty;    // Динамический штраф от находящихся в клетке агентов
    public ushort BestCost;      // Итоговая стоимость по Дейкстре
    public float3 BestDirection; // Вектор направления
    public float3 SurfaceNormal;
}
// Компонент для юнитов, которые будут двигаться по полю
public struct FlowAgent : IComponentData {
    public float MoveSpeed;
    public float AvoidanceRadius; // Радиус личного пространства (например, 0.6f)
    public float AvoidanceForce;  // Сила отталкивания (например, 3.0f)
}