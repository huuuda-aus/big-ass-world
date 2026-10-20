using System;
using System.Collections.Generic;

namespace Realm.Sim.Entities;

/// <summary>
/// The simulation's entities and their components. Entities are IDs, components are plain structs,
/// and systems read and write them through this class (CLAUDE.md 5.1). Holds no Godot types.
/// </summary>
/// <remarks>
/// Determinism: entity IDs are handed out in increasing order and never reused; queries and span
/// iteration return results in entity order. Thread-safety: not thread-safe; only the simulation tick
/// touches it.
/// Performance: components live in sorted contiguous arrays. For per-tick loops over many entities use
/// <see cref="EntitiesWith{T}"/> and <see cref="ComponentsOf{T}"/> (no allocation, no lookups);
/// <see cref="Query{T}"/>, <see cref="Set{T}"/> and <see cref="TryGet{T}"/> are for code that is not hot.
/// Measurements: docs/systems/sim.md section 8.
/// </remarks>
public sealed class SimWorld
{
    // Sorted ascending: IDs are created in increasing order, so creation is an append.
    private readonly List<EntityId> _alive = new();
    private readonly Dictionary<Type, IComponentStore> _stores = new();
    private long _nextId = 1;

    /// <summary>Creates an empty world.</summary>
    /// <param name="seed">Root seed of this world; systems derive their generators from it with <c>Rng.ForKey</c>.</param>
    public SimWorld(ulong seed) => Seed = seed;

    /// <summary>Root seed of this world.</summary>
    public ulong Seed { get; }

    /// <summary>Number of living entities.</summary>
    public int EntityCount => _alive.Count;

    /// <summary>All living entities in ID order.</summary>
    public IReadOnlyList<EntityId> Entities => _alive;

    /// <summary>Creates an entity.</summary>
    /// <returns>Its new, never-before-used ID.</returns>
    public EntityId CreateEntity()
    {
        var id = new EntityId(_nextId++);
        _alive.Add(id);
        return id;
    }

    /// <summary>Checks whether an entity exists.</summary>
    /// <param name="entity">The ID to check.</param>
    /// <returns><c>true</c> when the entity was created and not destroyed.</returns>
    public bool IsAlive(EntityId entity) => _alive.BinarySearch(entity) >= 0;

    /// <summary>Destroys an entity and all its components. Its ID is never reused.</summary>
    /// <param name="entity">The entity to destroy.</param>
    /// <returns><c>true</c> when the entity existed.</returns>
    public bool DestroyEntity(EntityId entity)
    {
        int index = _alive.BinarySearch(entity);
        if (index < 0)
        {
            return false;
        }

        _alive.RemoveAt(index);

        foreach (IComponentStore store in _stores.Values)
        {
            store.Remove(entity);
        }

        return true;
    }

    /// <summary>Adds or replaces a component on an entity.</summary>
    /// <typeparam name="T">Component type.</typeparam>
    /// <param name="entity">A living entity.</param>
    /// <param name="component">The component value (copied).</param>
    /// <exception cref="InvalidOperationException"><paramref name="entity"/> is not alive.</exception>
    public void Set<T>(EntityId entity, T component)
        where T : struct
    {
        if (!IsAlive(entity))
        {
            throw new InvalidOperationException($"Entity {entity} does not exist.");
        }

        Store<T>().Set(entity, component);
    }

    /// <summary>Reads a component.</summary>
    /// <typeparam name="T">Component type.</typeparam>
    /// <param name="entity">The entity.</param>
    /// <param name="component">The value when present.</param>
    /// <returns><c>true</c> when the entity has the component.</returns>
    public bool TryGet<T>(EntityId entity, out T component)
        where T : struct => Store<T>().TryGet(entity, out component);

    /// <summary>Checks whether an entity has a component.</summary>
    /// <typeparam name="T">Component type.</typeparam>
    /// <param name="entity">The entity.</param>
    /// <returns><c>true</c> when present.</returns>
    public bool Has<T>(EntityId entity)
        where T : struct => Store<T>().Has(entity);

    /// <summary>Removes a component from an entity.</summary>
    /// <typeparam name="T">Component type.</typeparam>
    /// <param name="entity">The entity.</param>
    public void Remove<T>(EntityId entity)
        where T : struct => Store<T>().Remove(entity);

    /// <summary>Lists every entity that has a component, with its value, in entity order.</summary>
    /// <typeparam name="T">Component type.</typeparam>
    /// <returns>
    /// A copy, so the world may be changed while the result is processed. The copy is not updated: if a
    /// listed entity is destroyed meanwhile it is still in the list, so check <see cref="IsAlive"/>
    /// before writing to it.
    /// </returns>
    public List<(EntityId Entity, T Value)> Query<T>()
        where T : struct => Store<T>().Snapshot();

    /// <summary>
    /// Entities that have component <typeparamref name="T"/>, in ID order. Index <c>i</c> here matches
    /// index <c>i</c> of <see cref="ComponentsOf{T}"/>. Allocation-free; use in per-tick loops.
    /// </summary>
    /// <typeparam name="T">Component type.</typeparam>
    /// <returns>
    /// A view of the live storage. It stays valid only until a component of this type is added or removed
    /// or one of its entities is destroyed; after that, stop using it and fetch a new one. Collect such
    /// changes during the loop and apply them afterwards.
    /// </returns>
    public ReadOnlySpan<EntityId> EntitiesWith<T>()
        where T : struct => Store<T>().Ids;

    /// <summary>
    /// The values of component <typeparamref name="T"/>, parallel to <see cref="EntitiesWith{T}"/>.
    /// Writing an element changes the component in place. Allocation-free; use in per-tick loops.
    /// </summary>
    /// <typeparam name="T">Component type.</typeparam>
    /// <returns>
    /// A view of the live storage, with the same validity rule as <see cref="EntitiesWith{T}"/>. It is
    /// memory-safe even if misused, but writes after a structural change may be lost.
    /// </returns>
    public Span<T> ComponentsOf<T>()
        where T : struct => Store<T>().Values;

    private ComponentStore<T> Store<T>()
        where T : struct
    {
        if (!_stores.TryGetValue(typeof(T), out IComponentStore? store))
        {
            store = new ComponentStore<T>();
            _stores[typeof(T)] = store;
        }

        return (ComponentStore<T>)store;
    }
}
