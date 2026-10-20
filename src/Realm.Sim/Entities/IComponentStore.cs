namespace Realm.Sim.Entities;

/// <summary>Type-erased view of a component store, so the world can remove a destroyed entity everywhere.</summary>
internal interface IComponentStore
{
    /// <summary>Removes the entity's component if present.</summary>
    void Remove(EntityId entity);
}
