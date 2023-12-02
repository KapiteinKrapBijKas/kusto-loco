﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace BabyKusto.Core;

/// <summary>
/// Represents a column formed of one or more sections which are processed in order
/// </summary>
/// <typeparam name="T"></typeparam>
public class ReassembledChunkColumn<T> : Column<T>
{
    private readonly record struct Section(int Offset, int Length,Column<T> BackingColumn);

    private readonly Section[] BackingColumns ;
    private readonly int _Length;

    public ReassembledChunkColumn(IEnumerable<Column<T>> backing)
        : base(backing.First().Type, Array.Empty<T>())
    {

        var sections = new List<Section>();
        var offset = 0;
        foreach (var b in backing)
        {
            var s = new Section(offset, b.RowCount, b);
            offset += b.RowCount;
            sections.Add(s);
        }

        BackingColumns = sections.ToArray();
        _Length = offset;
    }

    private (int,Column<T>) IndirectIndex(int index)
    {
        foreach (var s in BackingColumns)
        {
            if (index >= s.Offset && index < (s.Offset + s.Length))
                return (index - s.Offset, s.BackingColumn);
        }

        throw new InvalidOperationException($"Requested an index {index} which is greater than rowcount {RowCount} with {BackingColumns.Length} backing columns");
    }

    public override T? this[int index]
    {
        get
        {
            var (i, chunk) = IndirectIndex(index);
            return chunk[i];
        }
    }

    public override int RowCount => _Length;


   

    public override void ForEach(Action<object?> action)
    {
        for (var i = 0; i < RowCount; i++)
        {
            action(this[i]);
        }
    }

    public override Column Slice(int start, int length)
    {
        throw new NotImplementedException("Can't yet slice reassembled chunks");
    }


    public override object? GetRawDataValue(int index)
    {
        var (i, chunk) = IndirectIndex(index);
        return chunk[i];
    }
}