﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using Kusto.Language.Symbols;

namespace BabyKusto.Core.Evaluation.BuiltIns.Impl;

internal class NotStartsWithOperatorImpl : IScalarFunctionImpl
{
    public ScalarResult InvokeScalar(ScalarResult[] arguments)
    {
        Debug.Assert(arguments.Length == 2);
        var left = (string?)arguments[0].Value;
        var right = (string?)arguments[1].Value;
        return new ScalarResult(ScalarTypes.Bool,
            !(left ?? string.Empty).ToUpperInvariant().StartsWith((right ?? string.Empty).ToUpperInvariant()));
    }

    public ColumnarResult InvokeColumnar(ColumnarResult[] arguments)
    {
        Debug.Assert(arguments.Length == 2);
        Debug.Assert(arguments[0].Column.RowCount == arguments[1].Column.RowCount);
        var left = (TypedBaseColumn<string?>)(arguments[0].Column);
        var right = (TypedBaseColumn<string?>)(arguments[1].Column);

        var data = new bool?[left.RowCount];
        for (var i = 0; i < left.RowCount; i++)
        {
            data[i] = !(left[i] ?? string.Empty).ToUpperInvariant()
                .StartsWith((right[i] ?? string.Empty).ToUpperInvariant());
        }

        return new ColumnarResult(ColumnFactory.Create(ScalarTypes.Bool, data));
    }
}