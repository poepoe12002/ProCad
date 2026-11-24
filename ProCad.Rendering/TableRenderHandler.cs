using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Tables;
using CSMath;
using CSMath.Extensions;

namespace ProCad.Rendering;

public sealed class TableRenderHandler : IRenderEntityHandler
{
    public bool CanHandle(Entity entity) => entity is TableEntity;

    public void Append(Entity entity, Transform transform, RenderBuildContext context)
    {
        var table = (TableEntity)entity;
        if (table.Rows.Count == 0 || table.Columns.Count == 0)
        {
            return;
        }

        var basis = ResolveBasis(table);
        var xAxis = basis.XAxis;
        var yAxis = basis.YAxis;
        var origin = table.InsertPoint;

        var builder = context.GetLayerBuilder(table);
        var color = context.ResolveEntityColor(table);
        var thickness = context.ResolveLineWeight(table);
        var lineCap = context.ResolveLineCap(table);
        var lineJoin = context.ResolveLineJoin(table);

        var columnPositions = BuildColumnPositions(table.Columns.ToList());
        var rowPositions = BuildRowPositions(table.Rows.ToList());
        var spans = BuildCellSpans(table);
        var styles = BuildEffectiveStyles(table, spans);

        RenderBorders(
            table,
            builder,
            columnPositions,
            rowPositions,
            spans,
            styles,
            origin,
            xAxis,
            yAxis,
            transform,
            color,
            thickness,
            lineCap,
            lineJoin,
            context);

        RenderCells(
            table,
            columnPositions,
            rowPositions,
            spans,
            styles,
            origin,
            xAxis,
            yAxis,
            transform,
            context);
    }

    private static (XYZ XAxis, XYZ YAxis) ResolveBasis(TableEntity table)
    {
        var xAxis = table.HorizontalDirection;
        if (xAxis.IsZero())
        {
            xAxis = XYZ.AxisX;
        }

        xAxis = xAxis.Normalize();
        var normal = table.Normal.IsZero() ? XYZ.AxisZ : table.Normal.Normalize();
        var yAxis = XYZ.Cross(normal, xAxis);
        if (yAxis.IsZero())
        {
            yAxis = XYZ.AxisY;
        }
        else
        {
            yAxis = yAxis.Normalize();
        }

        return (xAxis, yAxis);
    }

    private static List<double> BuildColumnPositions(IReadOnlyList<TableEntity.Column> columns)
    {
        var positions = new List<double>(columns.Count + 1) { 0.0 };
        var x = 0.0;
        foreach (var column in columns)
        {
            x += Math.Max(0.0, column.Width);
            positions.Add(x);
        }

        return positions;
    }

    private static List<double> BuildRowPositions(IReadOnlyList<TableEntity.Row> rows)
    {
        var positions = new List<double>(rows.Count + 1) { 0.0 };
        var y = 0.0;
        foreach (var row in rows)
        {
            y += Math.Max(0.0, row.Height);
            positions.Add(y);
        }

        return positions;
    }

    private static void RenderBorders(
        TableEntity table,
        RenderLayerBuilder builder,
        IReadOnlyList<double> columnPositions,
        IReadOnlyList<double> rowPositions,
        CellSpanInfo[,] spans,
        EffectiveCellStyle[,] styles,
        XYZ origin,
        XYZ xAxis,
        XYZ yAxis,
        Transform transform,
        RenderColor color,
        float thickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin,
        RenderBuildContext context)
    {
        var rows = spans.GetLength(0);
        var cols = spans.GetLength(1);
        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                var span = spans[row, col];
                if (!span.IsMaster)
                {
                    continue;
                }

                var style = styles[row, col];
                var left = columnPositions[col];
                var right = columnPositions[Math.Min(col + span.ColumnSpan, columnPositions.Count - 1)];
                var top = rowPositions[row];
                var bottom = rowPositions[Math.Min(row + span.RowSpan, rowPositions.Count - 1)];

                var hasTopNeighbor = row > 0 && spans[row - 1, col].RegionId == span.RegionId;
                var hasBottomNeighbor = row + span.RowSpan < rows && spans[row + span.RowSpan, col].RegionId == span.RegionId;
                var hasLeftNeighbor = col > 0 && spans[row, col - 1].RegionId == span.RegionId;
                var hasRightNeighbor = col + span.ColumnSpan < cols && spans[row, col + span.ColumnSpan].RegionId == span.RegionId;

                if (!hasTopNeighbor)
                {
                    DrawHorizontalBorder(
                        builder,
                        style,
                        table,
                        EdgeType.Top,
                        origin,
                        xAxis,
                        yAxis,
                        left,
                        right,
                        top,
                        transform,
                        color,
                        thickness,
                        lineCap,
                        lineJoin,
                        context.Settings,
                        isOuter: row == 0);
                }

                if (!hasBottomNeighbor)
                {
                    DrawHorizontalBorder(
                        builder,
                        style,
                        table,
                        EdgeType.Bottom,
                        origin,
                        xAxis,
                        yAxis,
                        left,
                        right,
                        bottom,
                        transform,
                        color,
                        thickness,
                        lineCap,
                        lineJoin,
                        context.Settings,
                        isOuter: row + span.RowSpan >= rows);
                }

                if (!hasLeftNeighbor)
                {
                    DrawVerticalBorder(
                        builder,
                        style,
                        table,
                        EdgeType.Left,
                        origin,
                        xAxis,
                        yAxis,
                        left,
                        top,
                        bottom,
                        transform,
                        color,
                        thickness,
                        lineCap,
                        lineJoin,
                        context.Settings,
                        isOuter: col == 0);
                }

                if (!hasRightNeighbor)
                {
                    DrawVerticalBorder(
                        builder,
                        style,
                        table,
                        EdgeType.Right,
                        origin,
                        xAxis,
                        yAxis,
                        right,
                        top,
                        bottom,
                        transform,
                        color,
                        thickness,
                        lineCap,
                        lineJoin,
                        context.Settings,
                        isOuter: col + span.ColumnSpan >= cols);
                }
            }
        }
    }

    private static void RenderCells(
        TableEntity table,
        IReadOnlyList<double> columnPositions,
        IReadOnlyList<double> rowPositions,
        CellSpanInfo[,] spans,
        EffectiveCellStyle[,] styles,
        XYZ origin,
        XYZ xAxis,
        XYZ yAxis,
        Transform transform,
        RenderBuildContext context)
    {
        var tableStyle = table.Style ?? TableStyle.Default;

        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];

            for (var columnIndex = 0; columnIndex < table.Columns.Count; columnIndex++)
            {
                if (columnIndex >= row.Cells.Count)
                {
                    continue;
                }

                var span = spans[rowIndex, columnIndex];
                if (!span.IsMaster)
                {
                    continue;
                }

                var cell = row.Cells[columnIndex];
                var left = columnPositions[columnIndex];
                var right = columnPositions[Math.Min(columnIndex + span.ColumnSpan, columnPositions.Count - 1)];
                var top = rowPositions[rowIndex];
                var bottom = rowPositions[Math.Min(rowIndex + span.RowSpan, rowPositions.Count - 1)];

                var cellStyle = styles[rowIndex, columnIndex];
                var fillColor = ResolveCellFillColor(cellStyle, context.Settings.Background, context.Settings);
                if (cellStyle.IsFillColorOn)
                {
                    var quad = BuildCellQuad(origin, xAxis, yAxis, left, right, top, bottom, transform);
                    context.GetLayerBuilder(table).Add(new RenderFill(quad, fillColor));
                }

                var alignment = cellStyle.Alignment;
                var anchor = ResolveCellAnchor(
                    origin,
                    xAxis,
                    yAxis,
                    left,
                    right,
                    top,
                    bottom,
                    cellStyle.MarginLeft,
                    cellStyle.MarginTop,
                    cellStyle.MarginRight,
                    cellStyle.MarginBottom,
                    alignment);

                if (TryCreateCellBlock(table, cell, cellStyle, anchor, out var blockEntity))
                {
                    context.Dispatcher.Append(blockEntity!, transform, context);
                    continue;
                }

                var textValue = ResolveCellText(cell);
                if (string.IsNullOrWhiteSpace(textValue))
                {
                    continue;
                }

                var text = CreateCellText(table, cell, cellStyle, textValue, anchor, alignment);
                context.Dispatcher.Append(text, transform, context);
            }
        }
    }

    private static IReadOnlyList<System.Numerics.Vector2> BuildCellQuad(
        XYZ origin,
        XYZ xAxis,
        XYZ yAxis,
        double left,
        double right,
        double top,
        double bottom,
        Transform transform)
    {
        var points = new List<System.Numerics.Vector2>(4)
        {
            RenderTransformUtils.Apply(transform, origin + xAxis * left - yAxis * top),
            RenderTransformUtils.Apply(transform, origin + xAxis * right - yAxis * top),
            RenderTransformUtils.Apply(transform, origin + xAxis * right - yAxis * bottom),
            RenderTransformUtils.Apply(transform, origin + xAxis * left - yAxis * bottom)
        };

        return points;
    }

    private static RenderColor ResolveCellFillColor(
        EffectiveCellStyle cellStyle,
        RenderColor fallback,
        CadRenderSceneSettings settings)
    {
        var color = cellStyle.BackgroundColor;
        return RenderStyleUtils.ResolveColorOrFallback(color, settings, fallback);
    }

    private static string ResolveCellText(TableEntity.Cell cell)
    {
        if (cell.Contents is null || cell.Contents.Count == 0)
        {
            return string.Empty;
        }
        
        var lines = new List<string>(cell.Contents.Count);
        foreach (var content in cell.Contents)
        {
            if (content.ContentType == TableEntity.TableCellContentType.Block)
            {
                var blockLabel = ResolveContentText(content);
                if (!string.IsNullOrWhiteSpace(blockLabel))
                {
                    lines.Add(blockLabel);
                }
                continue;
            }

            var text = ResolveContentText(content);
            if (!string.IsNullOrWhiteSpace(text))
            {
                lines.Add(text);
            }
        }

        return lines.Count == 0 ? string.Empty : string.Join("\\P", lines);
    }

    private static string ResolveContentText(TableEntity.CellContent content)
    {
        var value = content.CadValue;
        if (!string.IsNullOrWhiteSpace(value.FormattedValue))
        {
            return value.FormattedValue;
        }

        if (value.Value is not null)
        {
            return value.Value.ToString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static XYZ ResolveCellAnchor(
        XYZ origin,
        XYZ xAxis,
        XYZ yAxis,
        double left,
        double right,
        double top,
        double bottom,
        double marginLeft,
        double marginTop,
        double marginRight,
        double marginBottom,
        TableStyle.CellAlignmentType alignment)
    {
        var innerLeft = left + marginLeft;
        var innerRight = right - marginRight;
        var innerTop = top + marginTop;
        var innerBottom = bottom - marginBottom;
        var centerX = (innerLeft + innerRight) * 0.5;
        var centerY = (innerTop + innerBottom) * 0.5;

        double x = centerX;
        double y = centerY;

        switch (alignment)
        {
            case TableStyle.CellAlignmentType.TopLeft:
                x = innerLeft;
                y = innerTop;
                break;
            case TableStyle.CellAlignmentType.TopCenter:
                x = centerX;
                y = innerTop;
                break;
            case TableStyle.CellAlignmentType.TopRight:
                x = innerRight;
                y = innerTop;
                break;
            case TableStyle.CellAlignmentType.MiddleLeft:
                x = innerLeft;
                y = centerY;
                break;
            case TableStyle.CellAlignmentType.MiddleCenter:
                x = centerX;
                y = centerY;
                break;
            case TableStyle.CellAlignmentType.MiddleRight:
                x = innerRight;
                y = centerY;
                break;
            case TableStyle.CellAlignmentType.BottomLeft:
                x = innerLeft;
                y = innerBottom;
                break;
            case TableStyle.CellAlignmentType.BottomCenter:
                x = centerX;
                y = innerBottom;
                break;
            case TableStyle.CellAlignmentType.BottomRight:
                x = innerRight;
                y = innerBottom;
                break;
        }

        return origin + xAxis * x - yAxis * y;
    }

    private static MText CreateCellText(
        TableEntity table,
        TableEntity.Cell cell,
        EffectiveCellStyle cellStyle,
        string textValue,
        XYZ anchor,
        TableStyle.CellAlignmentType alignment)
    {
        var format = cell.Content?.Format;
        var height = format?.TextHeight ?? cellStyle.TextHeight;
        if (height <= 0)
        {
            height = 1.0;
        }

        var textStyle = format?.TextStyle ?? cellStyle.TextStyle ?? TextStyle.Default;
        var color = ResolveCellTextColor(table, cellStyle, format);

        return new MText
        {
            Value = textValue,
            InsertPoint = anchor,
            Height = height,
            AttachmentPoint = ResolveAttachmentPoint(alignment),
            Color = color,
            Layer = table.Layer,
            Style = textStyle
        };
    }

    private static Color ResolveCellTextColor(
        TableEntity table,
        EffectiveCellStyle cellStyle,
        TableStyle.ContentFormat? format)
    {
        var color = format?.Color ?? cellStyle.TextColor;
        if (color.IsByLayer || color.IsByBlock)
        {
            return table.Color;
        }

        return color;
    }

    private static AttachmentPointType ResolveAttachmentPoint(TableStyle.CellAlignmentType alignment)
    {
        return alignment switch
        {
            TableStyle.CellAlignmentType.TopLeft => AttachmentPointType.TopLeft,
            TableStyle.CellAlignmentType.TopCenter => AttachmentPointType.TopCenter,
            TableStyle.CellAlignmentType.TopRight => AttachmentPointType.TopRight,
            TableStyle.CellAlignmentType.MiddleLeft => AttachmentPointType.MiddleLeft,
            TableStyle.CellAlignmentType.MiddleCenter => AttachmentPointType.MiddleCenter,
            TableStyle.CellAlignmentType.MiddleRight => AttachmentPointType.MiddleRight,
            TableStyle.CellAlignmentType.BottomLeft => AttachmentPointType.BottomLeft,
            TableStyle.CellAlignmentType.BottomCenter => AttachmentPointType.BottomCenter,
            TableStyle.CellAlignmentType.BottomRight => AttachmentPointType.BottomRight,
            _ => AttachmentPointType.MiddleCenter
        };
    }

    private static bool TryCreateCellBlock(
        TableEntity table,
        TableEntity.Cell cell,
        EffectiveCellStyle style,
        XYZ anchor,
        out Entity? entity)
    {
        entity = null;
        var content = cell.Content;
        if (content is null)
        {
            return false;
        }

        if (content.ContentType != TableEntity.TableCellContentType.Block && cell.Type != TableEntity.CellType.Block)
        {
            return false;
        }

        var block = ResolveCellBlock(table, content);
        if (block is null)
        {
            // If neither resolved block reference nor block name is present, fall back to text rendering.
            return false;
        }

        var scale = cell.BlockScale > 0 ? cell.BlockScale : 1.0;
        entity = new Insert(block)
        {
            InsertPoint = anchor,
            XScale = scale,
            YScale = scale,
            ZScale = scale,
            Rotation = cell.Rotation,
            Normal = table.Normal,
            Color = table.Color,
            LineWeight = table.LineWeight,
            Layer = table.Layer
        };
        return true;
    }

    private static BlockRecord? ResolveCellBlock(TableEntity table, TableEntity.CellContent content)
    {
        if (content.CadValue?.Value is BlockRecord record)
        {
            return record;
        }

        var document = table.Document;
        if (document is null)
        {
            return null;
        }

        var valueType = content.CadValue?.ValueType;
        var preferHandle = valueType == CadValueType.Handle;
        if (TryResolveBlockByHandleValue(document, content.CadValue?.Value, preferHandle, out var blockByValue))
        {
            return blockByValue;
        }

        var name = content.CadValue?.FormattedValue;
        if (string.IsNullOrWhiteSpace(name) &&
            content.CadValue?.Value is string valueName)
        {
            name = valueName;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (document.BlockRecords.TryGetValue(name, out var blockByName))
        {
            return blockByName;
        }

        return TryParseHandleToken(name, out var handle)
            ? document.GetCadObject<BlockRecord>(handle)
            : null;
    }

    private static bool TryResolveBlockByHandleValue(
        CadDocument document,
        object? value,
        bool preferHandle,
        out BlockRecord? block)
    {
        block = null;
        if (value is null)
        {
            return false;
        }

        switch (value)
        {
            case ulong handle:
                block = document.GetCadObject<BlockRecord>(handle);
                return block is not null;
            case long signedHandle when signedHandle > 0:
                block = document.GetCadObject<BlockRecord>((ulong)signedHandle);
                return block is not null;
            case int intHandle when intHandle > 0:
                block = document.GetCadObject<BlockRecord>((ulong)intHandle);
                return block is not null;
            case string token:
                var candidate = token.Trim();
                if (preferHandle)
                {
                    if (!TryParseHandleToken(candidate, out var preferredHandle))
                    {
                        return false;
                    }

                    block = document.GetCadObject<BlockRecord>(preferredHandle);
                    return block is not null;
                }

                if (document.BlockRecords.TryGetValue(candidate, out var blockByName))
                {
                    block = blockByName;
                    return true;
                }

                if (!TryParseHandleToken(candidate, out var parsedHandle))
                {
                    return false;
                }

                block = document.GetCadObject<BlockRecord>(parsedHandle);
                return block is not null;
            case IHandledCadObject handled when handled.Handle != 0:
                block = handled as BlockRecord ?? document.GetCadObject<BlockRecord>(handled.Handle);
                return block is not null;
            default:
                return false;
        }
    }

    private static bool TryParseHandleToken(string token, out ulong handle)
    {
        handle = 0;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var candidate = token.Trim();
        if (candidate.StartsWith("H:", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate[2..];
        }

        if (candidate.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate[2..];
        }

        return ulong.TryParse(candidate, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out handle) ||
               ulong.TryParse(candidate, NumberStyles.Integer, CultureInfo.InvariantCulture, out handle);
    }

    private static CellSpanInfo[,] BuildCellSpans(TableEntity table)
    {
        var rows = table.Rows.Count;
        var cols = table.Columns.Count;
        var spans = new CellSpanInfo[rows, cols];

        var regionId = 1;
        for (var row = 0; row < rows; row++)
        {
            var rowCells = table.Rows[row].Cells;
            for (var col = 0; col < cols; col++)
            {
                if (spans[row, col].IsAssigned)
                {
                    continue;
                }

                if (col >= rowCells.Count)
                {
                    spans[row, col] = CellSpanInfo.CreateMaster(regionId++, row, col, 1, 1);
                    continue;
                }

                var cell = rowCells[col];
                var spanX = Math.Max(1, cell.BorderWidth);
                var spanY = Math.Max(1, cell.BorderHeight);
                var isMaster = spanX > 1 || spanY > 1;
                if (!isMaster && cell.MergedValue != 0)
                {
                    isMaster = true;
                }

                if (!isMaster)
                {
                    spans[row, col] = CellSpanInfo.CreateMaster(regionId++, row, col, 1, 1);
                    continue;
                }

                var id = regionId++;
                spans[row, col] = CellSpanInfo.CreateMaster(id, row, col, spanY, spanX);

                for (var r = row; r < Math.Min(rows, row + spanY); r++)
                {
                    for (var c = col; c < Math.Min(cols, col + spanX); c++)
                    {
                        if (r == row && c == col)
                        {
                            continue;
                        }

                        spans[r, c] = CellSpanInfo.CreateCovered(id, row, col);
                    }
                }
            }
        }

        return spans;
    }

    private static EffectiveCellStyle[,] BuildEffectiveStyles(TableEntity table, CellSpanInfo[,] spans)
    {
        var rows = spans.GetLength(0);
        var cols = spans.GetLength(1);
        var styles = new EffectiveCellStyle[rows, cols];
        var cache = new Dictionary<(int Row, int Col), EffectiveCellStyle>();

        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                var span = spans[row, col];
                var key = (span.MasterRow, span.MasterCol);
                if (!cache.TryGetValue(key, out var style))
                {
                    var cell = table.Rows[span.MasterRow].Cells[span.MasterCol];
                    style = ResolveEffectiveStyle(table, span.MasterRow, span.MasterCol, cell);
                    cache[key] = style;
                }

                styles[row, col] = style;
            }
        }

        return styles;
    }

    private static EffectiveCellStyle ResolveEffectiveStyle(TableEntity table, int row, int col, TableEntity.Cell cell)
    {
        var tableStyle = table.Style ?? TableStyle.Default;
        var baseStyle = tableStyle.TableCellStyle ?? new TableStyle.CellStyle();
        var rowStyle = ResolveRowStyle(tableStyle, row);

        var effective = EffectiveCellStyle.FromBase(baseStyle);
        effective.ApplyBase(rowStyle);
        effective.SetVirtualEdges(cell);

        var columnOverride = table.Columns[col].CellStyleOverride;
        if (HasStyleOverrides(columnOverride))
        {
            effective.ApplyOverrides(columnOverride);
        }

        var rowOverride = table.Rows[row].CellStyleOverride;
        if (HasStyleOverrides(rowOverride))
        {
            effective.ApplyOverrides(rowOverride);
        }

        if (HasStyleOverrides(cell.StyleOverride))
        {
            effective.ApplyOverrides(cell.StyleOverride, allowHeuristics: true);
        }

        effective.ApplyMargins(tableStyle);
        return effective;
    }

    private static TableStyle.CellStyle ResolveRowStyle(TableStyle style, int rowIndex)
    {
        if (!style.SuppressTitle && rowIndex == 0)
        {
            return style.TitleCellStyle ?? new TableStyle.CellStyle();
        }

        var headerIndex = style.SuppressTitle ? 0 : 1;
        if (!style.SuppressHeaderRow && rowIndex == headerIndex)
        {
            return style.HeaderCellStyle ?? new TableStyle.CellStyle();
        }

        return style.DataCellStyle ?? new TableStyle.CellStyle();
    }

    private static bool HasStyleOverrides(TableStyle.CellStyle style)
    {
        if (style is null)
        {
            return false;
        }

        if (style.HasData)
        {
            return true;
        }

        if (style.PropertyOverrideFlags != TableStyle.CellStylePropertyFlags.None ||
            style.TableCellStylePropertyFlags != TableStyle.CellStylePropertyFlags.None ||
            style.MarginOverrideFlags != TableStyle.MarginFlags.None)
        {
            return true;
        }

        if (style.CellAlignment != TableStyle.CellAlignmentType.None ||
            style.TextHeight > 0 ||
            style.TextStyle is not null ||
            style.IsFillColorOn)
        {
            return true;
        }

        if (!style.BackgroundColor.IsByLayer && !style.BackgroundColor.IsByBlock)
        {
            return true;
        }

        if (!style.TextColor.IsByLayer && !style.TextColor.IsByBlock)
        {
            return true;
        }

        return HasBorderOverrides(style.TopBorder) ||
               HasBorderOverrides(style.BottomBorder) ||
               HasBorderOverrides(style.LeftBorder) ||
               HasBorderOverrides(style.RightBorder) ||
               HasBorderOverrides(style.HorizontalInsideBorder) ||
               HasBorderOverrides(style.VerticalInsideBorder);
    }

    private static bool HasBorderOverrides(TableStyle.CellBorder border)
    {
        return border.PropertyOverrideFlags != TableStyle.BorderPropertyFlags.None ||
               border.LineWeight != 0 ||
               border.DoubleLineSpacing > 0 ||
               border.IsInvisible ||
               (!border.Color.IsByLayer && !border.Color.IsByBlock);
    }

    private static void DrawHorizontalBorder(
        RenderLayerBuilder builder,
        EffectiveCellStyle style,
        TableEntity table,
        EdgeType edge,
        XYZ origin,
        XYZ xAxis,
        XYZ yAxis,
        double left,
        double right,
        double y,
        Transform transform,
        RenderColor baseColor,
        float baseThickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin,
        CadRenderSceneSettings settings,
        bool isOuter)
    {
        if (style.IsVirtual(edge))
        {
            return;
        }

        var border = style.ResolveBorder(edge, isOuter);
        if (border.IsInvisible)
        {
            return;
        }

        var start = origin + xAxis * left - yAxis * y;
        var end = origin + xAxis * right - yAxis * y;
        AddBorderLine(
            builder,
            RenderTransformUtils.Apply(transform, start),
            RenderTransformUtils.Apply(transform, end),
            border,
            baseColor,
            baseThickness,
            lineCap,
            lineJoin,
            settings,
            yAxis);
    }

    private static void DrawVerticalBorder(
        RenderLayerBuilder builder,
        EffectiveCellStyle style,
        TableEntity table,
        EdgeType edge,
        XYZ origin,
        XYZ xAxis,
        XYZ yAxis,
        double x,
        double top,
        double bottom,
        Transform transform,
        RenderColor baseColor,
        float baseThickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin,
        CadRenderSceneSettings settings,
        bool isOuter)
    {
        if (style.IsVirtual(edge))
        {
            return;
        }

        var border = style.ResolveBorder(edge, isOuter);
        if (border.IsInvisible)
        {
            return;
        }

        var start = origin + xAxis * x - yAxis * top;
        var end = origin + xAxis * x - yAxis * bottom;
        AddBorderLine(
            builder,
            RenderTransformUtils.Apply(transform, start),
            RenderTransformUtils.Apply(transform, end),
            border,
            baseColor,
            baseThickness,
            lineCap,
            lineJoin,
            settings,
            xAxis);
    }

    private static void AddBorderLine(
        RenderLayerBuilder builder,
        System.Numerics.Vector2 start,
        System.Numerics.Vector2 end,
        TableStyle.CellBorder border,
        RenderColor baseColor,
        float baseThickness,
        RenderLineCap lineCap,
        RenderLineJoin lineJoin,
        CadRenderSceneSettings settings,
        XYZ offsetAxis)
    {
        var color = ResolveBorderColor(border, baseColor, settings);
        var thickness = ResolveBorderThickness(border, baseThickness, settings);
        if (border.Type == TableStyle.BorderType.Double && border.DoubleLineSpacing > 0)
        {
            // Draw two parallel lines offset by half the spacing.
            var offset = (float)(border.DoubleLineSpacing * 0.5);
            var normal = new System.Numerics.Vector2((float)offsetAxis.X, (float)offsetAxis.Y);
            if (normal.LengthSquared() > 0)
            {
                normal = System.Numerics.Vector2.Normalize(normal) * offset;
                builder.Add(new RenderLine(start + normal, end + normal, color, thickness, lineCap, lineJoin));
                builder.Add(new RenderLine(start - normal, end - normal, color, thickness, lineCap, lineJoin));
                return;
            }
        }

        builder.Add(new RenderLine(start, end, color, thickness, lineCap, lineJoin));
    }

    private static RenderColor ResolveBorderColor(
        TableStyle.CellBorder border,
        RenderColor baseColor,
        CadRenderSceneSettings settings)
    {
        var borderColor = border.Color;
        if (borderColor.IsByLayer || borderColor.IsByBlock)
        {
            return baseColor;
        }
        return RenderStyleUtils.ResolveColor(borderColor, settings, baseColor.A);
    }

    private static float ResolveBorderThickness(
        TableStyle.CellBorder border,
        float baseThickness,
        CadRenderSceneSettings settings)
    {
        var lineWeight = border.LineWeight;
        if (lineWeight == 0 || lineWeight == LineWeightType.ByLayer || lineWeight == LineWeightType.ByBlock)
        {
            return baseThickness;
        }

        return RenderStyleUtils.ResolveLineWeight(lineWeight, settings);
    }

    private enum EdgeType
    {
        Top,
        Bottom,
        Left,
        Right
    }

    private readonly struct CellSpanInfo
    {
        public int RegionId { get; }
        public int MasterRow { get; }
        public int MasterCol { get; }
        public int RowSpan { get; }
        public int ColumnSpan { get; }
        public bool IsMaster { get; }
        public bool IsAssigned => RegionId != 0;

        private CellSpanInfo(int regionId, int masterRow, int masterCol, int rowSpan, int columnSpan, bool isMaster)
        {
            RegionId = regionId;
            MasterRow = masterRow;
            MasterCol = masterCol;
            RowSpan = rowSpan;
            ColumnSpan = columnSpan;
            IsMaster = isMaster;
        }

        public static CellSpanInfo CreateMaster(int regionId, int row, int col, int rowSpan, int colSpan)
            => new CellSpanInfo(regionId, row, col, rowSpan, colSpan, true);

        public static CellSpanInfo CreateCovered(int regionId, int masterRow, int masterCol)
            => new CellSpanInfo(regionId, masterRow, masterCol, 1, 1, false);
    }

    private sealed class EffectiveCellStyle
    {
        public TableStyle.CellAlignmentType Alignment { get; private set; }
        public double TextHeight { get; private set; }
        public TextStyle? TextStyle { get; private set; }
        public Color TextColor { get; private set; }
        public bool IsFillColorOn { get; private set; }
        public Color BackgroundColor { get; private set; }
        public double MarginLeft { get; private set; }
        public double MarginRight { get; private set; }
        public double MarginTop { get; private set; }
        public double MarginBottom { get; private set; }
        public TableStyle.CellBorder TopBorder { get; private set; }
        public TableStyle.CellBorder BottomBorder { get; private set; }
        public TableStyle.CellBorder LeftBorder { get; private set; }
        public TableStyle.CellBorder RightBorder { get; private set; }
        public TableStyle.CellBorder HorizontalInsideBorder { get; private set; }
        public TableStyle.CellBorder VerticalInsideBorder { get; private set; }
        public VirtualEdgeFlags VirtualEdges { get; private set; }

        private EffectiveCellStyle()
        {
            Alignment = TableStyle.CellAlignmentType.MiddleCenter;
            TextHeight = 0;
            TextColor = Color.ByLayer;
            BackgroundColor = Color.ByLayer;
            TopBorder = new TableStyle.CellBorder(TableStyle.CellEdgeFlags.Top);
            BottomBorder = new TableStyle.CellBorder(TableStyle.CellEdgeFlags.Bottom);
            LeftBorder = new TableStyle.CellBorder(TableStyle.CellEdgeFlags.Left);
            RightBorder = new TableStyle.CellBorder(TableStyle.CellEdgeFlags.Right);
            HorizontalInsideBorder = new TableStyle.CellBorder(TableStyle.CellEdgeFlags.InsideHorizontal);
            VerticalInsideBorder = new TableStyle.CellBorder(TableStyle.CellEdgeFlags.InsideVertical);
        }

        public static EffectiveCellStyle FromBase(TableStyle.CellStyle style)
        {
            var effective = new EffectiveCellStyle();
            effective.ApplyAll(style);
            return effective;
        }

        public void ApplyBase(TableStyle.CellStyle style)
        {
            ApplyAll(style);
        }

        public void ApplyOverrides(TableStyle.CellStyle style, bool allowHeuristics = false)
        {
            var flags = style.PropertyOverrideFlags;
            if (flags == TableStyle.CellStylePropertyFlags.None && allowHeuristics)
            {
                flags = DeriveOverrideFlags(style);
            }

            if (flags.HasFlag(TableStyle.CellStylePropertyFlags.Alignment) && style.CellAlignment != TableStyle.CellAlignmentType.None)
            {
                Alignment = style.CellAlignment;
            }

            if (flags.HasFlag(TableStyle.CellStylePropertyFlags.TextHeight) && style.TextHeight > 0)
            {
                TextHeight = style.TextHeight;
            }

            if (flags.HasFlag(TableStyle.CellStylePropertyFlags.TextStyle) && style.TextStyle is not null)
            {
                TextStyle = style.TextStyle;
            }

            if (flags.HasFlag(TableStyle.CellStylePropertyFlags.ContentColor) && !style.TextColor.IsByLayer && !style.TextColor.IsByBlock)
            {
                TextColor = style.TextColor;
            }

            if (flags.HasFlag(TableStyle.CellStylePropertyFlags.BackgroundColor))
            {
                BackgroundColor = style.BackgroundColor;
                IsFillColorOn = style.IsFillColorOn;
            }

            if (style.MarginOverrideFlags.HasFlag(TableStyle.MarginFlags.Override))
            {
                if (style.VerticalMargin > 0)
                {
                    MarginTop = style.VerticalMargin;
                    MarginBottom = style.VerticalMargin;
                }

                if (style.HorizontalMargin > 0)
                {
                    MarginLeft = style.HorizontalMargin;
                    MarginRight = style.HorizontalMargin;
                }

                if (style.BottomMargin > 0)
                {
                    MarginBottom = style.BottomMargin;
                }

                if (style.RightMargin > 0)
                {
                    MarginRight = style.RightMargin;
                }
            }

            ApplyBorderOverrides(TopBorder, style.TopBorder);
            ApplyBorderOverrides(BottomBorder, style.BottomBorder);
            ApplyBorderOverrides(LeftBorder, style.LeftBorder);
            ApplyBorderOverrides(RightBorder, style.RightBorder);
            ApplyBorderOverrides(HorizontalInsideBorder, style.HorizontalInsideBorder);
            ApplyBorderOverrides(VerticalInsideBorder, style.VerticalInsideBorder);
        }

        public void ApplyMargins(TableStyle tableStyle)
        {
            if (MarginLeft <= 0)
            {
                MarginLeft = tableStyle.HorizontalCellMargin;
            }
            if (MarginRight <= 0)
            {
                MarginRight = tableStyle.HorizontalCellMargin;
            }
            if (MarginTop <= 0)
            {
                MarginTop = tableStyle.VerticalCellMargin;
            }
            if (MarginBottom <= 0)
            {
                MarginBottom = tableStyle.VerticalCellMargin;
            }
        }

        public bool IsVirtual(EdgeType edge)
        {
            return edge switch
            {
                EdgeType.Top => VirtualEdges.HasFlag(VirtualEdgeFlags.Top),
                EdgeType.Bottom => VirtualEdges.HasFlag(VirtualEdgeFlags.Bottom),
                EdgeType.Left => VirtualEdges.HasFlag(VirtualEdgeFlags.Left),
                EdgeType.Right => VirtualEdges.HasFlag(VirtualEdgeFlags.Right),
                _ => false
            };
        }

        public void SetVirtualEdges(TableEntity.Cell cell)
        {
            VirtualEdges = (VirtualEdgeFlags)cell.VirtualEdgeFlag;
        }

        public TableStyle.CellBorder ResolveBorder(EdgeType edge, bool isOuter)
        {
            return edge switch
            {
                EdgeType.Top => SelectBorder(TopBorder, HorizontalInsideBorder, isOuter),
                EdgeType.Bottom => SelectBorder(BottomBorder, HorizontalInsideBorder, isOuter),
                EdgeType.Left => SelectBorder(LeftBorder, VerticalInsideBorder, isOuter),
                EdgeType.Right => SelectBorder(RightBorder, VerticalInsideBorder, isOuter),
                _ => TopBorder
            };
        }

        private static TableStyle.CellBorder SelectBorder(
            TableStyle.CellBorder edgeBorder,
            TableStyle.CellBorder insideBorder,
            bool isOuter)
        {
            if (isOuter)
            {
                return edgeBorder;
            }

            return edgeBorder.PropertyOverrideFlags != TableStyle.BorderPropertyFlags.None ? edgeBorder : insideBorder;
        }

        private void ApplyAll(TableStyle.CellStyle style)
        {
            if (style.CellAlignment != TableStyle.CellAlignmentType.None)
            {
                Alignment = style.CellAlignment;
            }

            if (style.TextHeight > 0)
            {
                TextHeight = style.TextHeight;
            }

            if (style.TextStyle is not null)
            {
                TextStyle = style.TextStyle;
            }

            TextColor = style.TextColor;
            BackgroundColor = style.BackgroundColor;
            IsFillColorOn = style.IsFillColorOn;

            MarginLeft = style.HorizontalMargin;
            MarginRight = style.HorizontalMargin;
            MarginTop = style.VerticalMargin;
            MarginBottom = style.VerticalMargin;

            CopyBorder(TopBorder, style.TopBorder);
            CopyBorder(BottomBorder, style.BottomBorder);
            CopyBorder(LeftBorder, style.LeftBorder);
            CopyBorder(RightBorder, style.RightBorder);
            CopyBorder(HorizontalInsideBorder, style.HorizontalInsideBorder);
            CopyBorder(VerticalInsideBorder, style.VerticalInsideBorder);
        }

        private static void CopyBorder(TableStyle.CellBorder target, TableStyle.CellBorder source)
        {
            if (source is null)
            {
                return;
            }

            target.Color = source.Color;
            target.LineWeight = source.LineWeight;
            target.IsInvisible = source.IsInvisible;
            target.DoubleLineSpacing = source.DoubleLineSpacing;
            target.Type = source.Type;
            target.PropertyOverrideFlags = source.PropertyOverrideFlags;
        }

        private static void ApplyBorderOverrides(TableStyle.CellBorder target, TableStyle.CellBorder source)
        {
            if (source is null)
            {
                return;
            }

            if (source.PropertyOverrideFlags == TableStyle.BorderPropertyFlags.None)
            {
                return;
            }

            if (source.PropertyOverrideFlags.HasFlag(TableStyle.BorderPropertyFlags.Color))
            {
                target.Color = source.Color;
            }

            if (source.PropertyOverrideFlags.HasFlag(TableStyle.BorderPropertyFlags.LineWeight))
            {
                target.LineWeight = source.LineWeight;
            }

            if (source.PropertyOverrideFlags.HasFlag(TableStyle.BorderPropertyFlags.Invisibility))
            {
                target.IsInvisible = source.IsInvisible;
            }

            if (source.PropertyOverrideFlags.HasFlag(TableStyle.BorderPropertyFlags.DoubleLineSpacing))
            {
                target.DoubleLineSpacing = source.DoubleLineSpacing;
                target.Type = source.Type;
            }
        }

        private static TableStyle.CellStylePropertyFlags DeriveOverrideFlags(TableStyle.CellStyle style)
        {
            var flags = TableStyle.CellStylePropertyFlags.None;
            if (style.CellAlignment != TableStyle.CellAlignmentType.None)
            {
                flags |= TableStyle.CellStylePropertyFlags.Alignment;
            }
            if (style.TextHeight > 0)
            {
                flags |= TableStyle.CellStylePropertyFlags.TextHeight;
            }
            if (style.TextStyle is not null)
            {
                flags |= TableStyle.CellStylePropertyFlags.TextStyle;
            }
            if (!style.TextColor.IsByLayer && !style.TextColor.IsByBlock)
            {
                flags |= TableStyle.CellStylePropertyFlags.ContentColor;
            }
            if (!style.BackgroundColor.IsByLayer && !style.BackgroundColor.IsByBlock)
            {
                flags |= TableStyle.CellStylePropertyFlags.BackgroundColor;
            }
            return flags;
        }
    }

    // ACadSharp's TableEntity.Cell.VirtualEdgeFlag is a raw short since the enum was removed
    // upstream (DomCR/ACadSharp 7c6f7e99); these are the values that enum used.
    [System.Flags]
    private enum VirtualEdgeFlags
    {
        None = 0,
        Top = 1,
        Right = 2,
        Bottom = 4,
        Left = 8
    }

}
