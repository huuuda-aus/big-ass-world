using System;
using System.Collections.Generic;

namespace Realm.Sim.Entities;

/// <summary>
/// Holds one component type for all entities in two parallel arrays kept sorted by
/// <see cref="EntityId"/>. Lookups binary-search contiguous memory; entities are normally added in
/// increasing ID order, which is an append.
/// </summary>
/// <typeparam name="T">Plain-data component type.</typeparam>
internal sealed class ComponentStore<T> : IComponentStore
    where T : struct
{
    private const int InitialCapacity = 8;

    private EntityId[] _ids = new EntityId[InitialCapacity];
    private T[] _values = new T[InitialCapacity];
    private int _count;

    public int Count => _count;

    /// <summary>Entities that have this component, in ID order. Valid until a component is added or removed.</summary>
    public ReadOnlySpan<EntityId> Ids => new(_ids, 0, _count);

    /// <summary>Component values parallel to <see cref="Ids"/>. Valid until a component is added or removed.</summary>
    public Span<T> Values => new(_values, 0, _count);

    public void Set(EntityId entity, T value)
    {
        int index = Find(entity);
        if (index >= 0)
        {
            _values[index] = value;
            return;
        }

        Insert(~index, entity, value);
    }

    public bool TryGet(EntityId entity, out T value)
    {
        int index = Find(entity);
        if (index >= 0)
        {
            value = _values[index];
            return true;
        }

        value = default;
        return false;
    }

    public bool Has(EntityId entity) => Find(entity) >= 0;

    public void Remove(EntityId entity)
    {
        int index = Find(entity);
        if (index < 0)
        {
            return;
        }

        _count--;
        if (index < _count)
        {
            Array.Copy(_ids, index + 1, _ids, index, _count - index);
            Array.Copy(_values, index + 1, _values, index, _count - index);
        }

        _ids[_count] = default;
        _values[_count] = default;
    }

    /// <summary>Copies the contents in entity order, so callers may change the store while iterating.</summary>
    public List<(EntityId Entity, T Value)> Snapshot()
    {
        var list = new List<(EntityId, T)>(_count);
        for (int i = 0; i < _count; i++)
        {
            list.Add((_ids[i], _values[i]));
        }

        return list;
    }

    // Binary search; returns the index, or the bitwise complement of the insertion point.
    private int Find(EntityId entity)
    {
        int low = 0;
        int high = _count - 1;
        while (low <= high)
        {
            int mid = (low + high) >>> 1;
            long order = _ids[mid].Value - entity.Value;
            if (order == 0)
            {
                return mid;
            }

            if (order < 0)
            {
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return ~low;
    }

    private void Insert(int index, EntityId entity, T value)
    {
        if (_count == _ids.Length)
        {
            Array.Resize(ref _ids, _count * 2);
            Array.Resize(ref _values, _count * 2);
        }

        if (index < _count)
        {
            Array.Copy(_ids, index, _ids, index + 1, _count - index);
            Array.Copy(_values, index, _values, index + 1, _count - index);
        }

        _ids[index] = entity;
        _values[index] = value;
        _count++;
    }
}
