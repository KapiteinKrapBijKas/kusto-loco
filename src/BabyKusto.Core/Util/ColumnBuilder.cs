﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Kusto.Language.Symbols;
using Microsoft.Extensions.Internal;

namespace BabyKusto.Core.Util;

public abstract class ColumnBuilder
{
    public abstract int RowCount { get; }
    public abstract object? this[int index] { get; }
    public abstract void Add(object? value);
    public abstract void AddRange(ColumnBuilder other);
    public abstract Column ToColumn();
    public abstract ColumnBuilder Clone();
    public abstract ColumnBuilder NewEmpty();
}

public abstract class IndirectColumnBuilder
{
    public abstract Column CreateIndirectColumn(int[] rows);
}

public class IndirectColumnBuilder<T> : IndirectColumnBuilder
{
    private readonly Column<T> _baseColumn;
    private readonly IndirectPolicy _policy;
    private readonly int[] _previousLookups;

    public IndirectColumnBuilder(Column<T> baseColumn, IndirectPolicy policy)
        : this(baseColumn, policy, Array.Empty<int>())
    {
    }


    public IndirectColumnBuilder(Column<T> baseColumn, IndirectPolicy policy, int[] previousLookups)
    {
        _baseColumn = baseColumn;
        _policy = policy;
        _previousLookups = previousLookups;
    }

    private IndirectColumn<T> Create(int[] rows) =>
        //TODO - this probably isn't worth doing because it prevents us sharing the 
        //lookup tables across multiple columns
        //if (_previousLookups.Any())
        //    rows = rows.Select(r => _previousLookups[r]).ToArray();
        new(rows, _baseColumn);

    public override Column CreateIndirectColumn(int[] rows)
    {
        return _policy switch
        {
            IndirectPolicy.Map =>
                Create(rows),
            IndirectPolicy.Passthru => _baseColumn,
            //when single value columns are sliced the mapping is unimportant
            IndirectPolicy.SingleValue => _baseColumn.Slice(0, rows.Length),
            _ => throw new NotImplementedException()
        };
    }
}

public class ColumnBuilder<T> : ColumnBuilder
{
    private readonly List<T?> _data = new();

    public ColumnBuilder(TypeSymbol type) => Type = type;

    public TypeSymbol Type { get; }

    public override int RowCount => _data.Count;
    public override object? this[int index] => _data[index];

    public void Add(T value)
    {
        _data.Add(value);
    }

    public override void AddRange(ColumnBuilder other)
    {
        if (other is not ColumnBuilder<T> typedOther)
        {
            throw new ArgumentException(
                $"Expected other of type {TypeNameHelper.GetTypeDisplayName(typeof(ColumnBuilder<T>))}, found {TypeNameHelper.GetTypeDisplayName(other)}");
        }

        _data.AddRange(typedOther._data);
    }

    public override void Add(object? value)
    {
        _data.Add((T?)value);
    }

    public override Column ToColumn() => Column.Create(Type, _data.ToArray());

    public override ColumnBuilder Clone()
    {
        var result = new ColumnBuilder<T>(Type);
        result.AddRange(this);
        return result;
    }

    public override ColumnBuilder NewEmpty()
    {
        var result = new ColumnBuilder<T>(Type);
        return result;
    }
}