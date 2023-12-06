﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using BabyKusto.Core.Extensions;
using BabyKusto.Core.InternalRepresentation;
using BabyKusto.Core.Util;
using Kusto.Language.Symbols;
using NLog;

namespace BabyKusto.Core.Evaluation.BuiltIns;

internal static class BuiltInsHelper
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    internal static T? PickOverload<T>(IReadOnlyList<T> overloads, IRExpressionNode[] arguments)
        where T : OverloadInfoBase
    {
        foreach (var overload in overloads)
        {
            if (overload.ParameterTypes.Count != arguments.Length)
            {
                continue;
            }

            var compatible = true;
            for (var i = 0; i < arguments.Length; i++)
            {
                var argument = arguments[i];
                var parameterType = overload.ParameterTypes[i];

                var simplifiedArgType = argument.ResultType.Simplify();
                var simplifiedParamType = parameterType.Simplify();

                var thisCompatible =
                    simplifiedArgType == simplifiedParamType ||
                    (simplifiedArgType is ScalarSymbol scalarArg && simplifiedParamType is ScalarSymbol scalarParam &&
                     scalarParam.IsWiderThan(scalarArg)) ||
                    simplifiedParamType == ScalarTypes.String; // TODO: Is it true that anything is coercible to string?

                if (!thisCompatible)
                {
                    compatible = false;
                    break;
                }
            }

            if (!compatible)
            {
                continue;
            }

            return overload;
        }

        return null;
    }

    // TODO: Support named parameters
    public static Func<EvaluationResult[], EvaluationResult> GetScalarImplementation(
        IRExpressionNode[] argumentExpressions, IScalarFunctionImpl impl, EvaluatedExpressionKind resultKind,
        TypeSymbol expectedResultType)
    {
        if (resultKind == EvaluatedExpressionKind.Scalar)
        {
            var expectedNumArgs = argumentExpressions.Length;
            return arguments =>
            {
                Debug.Assert(arguments.Length == expectedNumArgs);
                var scalarArgs = new ScalarResult[arguments.Length];
                for (var i = 0; i < arguments.Length; i++)
                {
                    scalarArgs[i] = (ScalarResult)arguments[i];
                }

                var result = impl.InvokeScalar(scalarArgs);
                Debug.Assert(result.Type.Simplify() == expectedResultType.Simplify(),
                    $"Evaluation produced wrong type {SchemaDisplay.GetText(result.Type)}, expected {SchemaDisplay.GetText(expectedResultType)}");
                return result;
            };
        }

        if (resultKind == EvaluatedExpressionKind.Columnar)
        {
            var firstColumnarArgIndex = -1;
            var argNeedsExpansion = new bool[argumentExpressions.Length];
            for (var i = 0; i < argumentExpressions.Length; i++)
            {
                if (argumentExpressions[i].ResultKind == EvaluatedExpressionKind.Columnar)
                {
                    if (firstColumnarArgIndex < 0)
                    {
                        firstColumnarArgIndex = i;
                    }
                }
                else
                {
                    argNeedsExpansion[i] = true;
                }
            }

            Debug.Assert(firstColumnarArgIndex >= 0);
            if (firstColumnarArgIndex < 0)
            {
                throw new InvalidOperationException();
            }

            return arguments =>
            {
                Debug.Assert(arguments.Length == argNeedsExpansion.Length);

                var numRows = ((ColumnarResult)arguments[firstColumnarArgIndex]).Column.RowCount;
                var columnarArgs = new ColumnarResult[arguments.Length];
                for (var i = 0; i < arguments.Length; i++)
                {
                    if (!argNeedsExpansion[i])
                    {
                        columnarArgs[i] = (ColumnarResult)arguments[i];
                    }
                    else
                    {
                        var scalarValue = (ScalarResult)arguments[i];
                        columnarArgs[i] =
                            new ColumnarResult(ColumnHelpers.CreateFromScalar(scalarValue.Value, scalarValue.Type, numRows));
                    }
                }

                //TODO NPM here -this should be parallisable for at least some operations
                //i.e. 1->1 mappings
                //other things such as max/min would need a map/reduce type approach
                //but possibly they are handled elsewhere
                var result = impl.InvokeColumnar(columnarArgs);
                Debug.Assert(result.Type.Simplify() == expectedResultType.Simplify(),
                    $"Evaluation produced wrong type {SchemaDisplay.GetText(result.Type)}, expected {SchemaDisplay.GetText(expectedResultType)}");
                return result;
            };
        }

        throw new InvalidOperationException($"Unexpected result kind {resultKind}");
    }

    public static Func<EvaluationResult[], EvaluationResult> GetWindowImplementation(
        IRExpressionNode[] argumentExpressions, IWindowFunctionImpl impl, EvaluatedExpressionKind resultKind,
        TypeSymbol expectedResultType)
    {
        if (resultKind != EvaluatedExpressionKind.Columnar)
        {
            throw new InvalidOperationException($"Unexpected result kind {resultKind}");
        }

        var firstColumnarArgIndex = -1;
        var argNeedsExpansion = new bool[argumentExpressions.Length];
        for (var i = 0; i < argumentExpressions.Length; i++)
        {
            if (argumentExpressions[i].ResultKind == EvaluatedExpressionKind.Columnar)
            {
                if (firstColumnarArgIndex < 0)
                {
                    firstColumnarArgIndex = i;
                }
            }
            else
            {
                argNeedsExpansion[i] = true;
            }
        }

        Debug.Assert(firstColumnarArgIndex >= 0);
        if (firstColumnarArgIndex < 0)
        {
            throw new InvalidOperationException();
        }

        ColumnarResult[]? lastWindowArgs = null;
        ColumnarResult? previousResult = null;
        return arguments =>
        {
            Debug.Assert(arguments.Length == argNeedsExpansion.Length);

            var numRows = ((ColumnarResult)arguments[firstColumnarArgIndex]).Column.RowCount;
            var columnarArgs = new ColumnarResult[arguments.Length];
            for (var i = 0; i < arguments.Length; i++)
            {
                if (!argNeedsExpansion[i])
                {
                    columnarArgs[i] = (ColumnarResult)arguments[i];
                }
                else
                {
                    var scalarValue = (ScalarResult)arguments[i];
                    columnarArgs[i] =
                        new ColumnarResult(ColumnHelpers.CreateFromScalar(scalarValue.Value, scalarValue.Type,
                            numRows));
                }
            }

            var result = impl.InvokeWindow(columnarArgs, lastWindowArgs, previousResult);
            Debug.Assert(result.Type.Simplify() == expectedResultType.Simplify(),
                $"Evaluation produced wrong type {SchemaDisplay.GetText(result.Type)}, expected {SchemaDisplay.GetText(expectedResultType)}");
            lastWindowArgs = columnarArgs;
            previousResult = result;
            return result;
        };
    }
}