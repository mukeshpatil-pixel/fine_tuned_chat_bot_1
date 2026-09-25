namespace SensorReportBot.Infrastructure.Services;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SensorReportBot.Application.DTOs;
using SensorReportBot.Application.Interfaces;

public class QuestPdfReportService : IPdfReportService
{
    static QuestPdfReportService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.UseSystemFonts = true;
        QuestPDF.Settings.ThrowOnMissingFontFamilies = false;
    }

    public Task<byte[]> GeneratePdfAsync(ReportDataDto data, CancellationToken ct = default)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(9.5f).FontColor(Colors.Grey.Darken3));

                // 1. HEADER
                page.Header().Element(header => ComposeHeader(header, data));

                // 2. CONTENT
                page.Content().PaddingVertical(10).Element(content => ComposeContent(content, data));

                // 3. FOOTER
                page.Footer().Element(ComposeFooter);
            });
        });

        byte[] pdfBytes = document.GeneratePdf();
        return Task.FromResult(pdfBytes);
    }

    private void ComposeHeader(IContainer container, ReportDataDto data)
    {
        container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingBottom(12).Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text("INDUSTRIAL ASSET TELEMETRY REPORT").FontSize(16).Bold().FontColor(Colors.Indigo.Darken2);
                col.Item().PaddingTop(2).Text($"{data.Asset.Name}  •  {data.Asset.Location ?? "Main Plant"}").FontSize(11).SemiBold();
                col.Item().PaddingTop(2).Text($"Timeframe: {data.From:yyyy-MM-dd HH:mm} to {data.To:yyyy-MM-dd HH:mm} (UTC)").FontSize(8.5f).FontColor(Colors.Grey.Darken1);
            });

            row.ConstantItem(120).Column(col =>
            {
                col.Item().AlignRight().Text($"Report ID: REP-{DateTime.UtcNow.Ticks % 1000000:D6}").FontSize(8).FontColor(Colors.Grey.Medium);
                col.Item().AlignRight().PaddingTop(4).Container()
                    .Background(data.HealthScore >= 80 ? Colors.Green.Lighten5 : (data.HealthScore >= 60 ? Colors.Amber.Lighten5 : Colors.Red.Lighten5))
                    .Border(1)
                    .BorderColor(data.HealthScore >= 80 ? Colors.Green.Medium : (data.HealthScore >= 60 ? Colors.Amber.Medium : Colors.Red.Medium))
                    .PaddingHorizontal(8).PaddingVertical(3).Text($"{data.HealthScore}% HEALTH ({data.HealthStatus.ToUpper()})")
                    .FontSize(8.5f).Bold()
                    .FontColor(data.HealthScore >= 80 ? Colors.Green.Darken2 : (data.HealthScore >= 60 ? Colors.Amber.Darken2 : Colors.Red.Darken2));
            });
        });
    }

    private void ComposeContent(IContainer container, ReportDataDto data)
    {
        container.Column(col =>
        {
            // ==========================================
            // PAGE 1: ASSET SUMMARY, SIGNALS STATS & TREND CHARTS
            // ==========================================

            // 1. Executive KPI Cards
            col.Item().Row(row =>
            {
                ComposeKpiCard(row.RelativeItem(), "Signals Monitored", data.Signals.Count.ToString(), Colors.Blue.Lighten5, Colors.Blue.Darken2);
                row.ConstantItem(8);
                ComposeKpiCard(row.RelativeItem(), "Threshold Breaches", data.Signals.Count(s => s.HasViolation).ToString(), data.Signals.Any(s => s.HasViolation) ? Colors.Red.Lighten5 : Colors.Green.Lighten5, data.Signals.Any(s => s.HasViolation) ? Colors.Red.Darken2 : Colors.Green.Darken2);
                row.ConstantItem(8);
                ComposeKpiCard(row.RelativeItem(), "Excursion Events", data.Events.Count.ToString(), Colors.Orange.Lighten5, Colors.Orange.Darken2);
                row.ConstantItem(8);
                ComposeKpiCard(row.RelativeItem(), "System Alerts", data.Alerts.Count.ToString(), Colors.Purple.Lighten5, Colors.Purple.Darken2);
            });

            // 2. Diagnostic & Engineering Insights
            if (data.KeyInsights.Any())
            {
                col.Item().PaddingTop(10).Container()
                    .Background(Colors.Grey.Lighten4)
                    .Border(1).BorderColor(Colors.Grey.Lighten2)
                    .Padding(8)
                    .Column(c =>
                    {
                        c.Item().Text("DIAGNOSTIC & ENGINEERING INSIGHTS").FontSize(9.5f).Bold().FontColor(Colors.Indigo.Darken2);
                        foreach (var insight in data.KeyInsights.Take(3))
                        {
                            c.Item().PaddingTop(2).Row(r =>
                            {
                                r.ConstantItem(12).Text("•").Bold().FontColor(Colors.Indigo.Medium);
                                r.RelativeItem().Text(insight).FontSize(8.5f);
                            });
                        }
                    });
            }

            // 3. Signals Statistical Summary Table (Asset Name in header, signals with Min, Max, Mean, Limits)
            col.Item().PaddingTop(12).Text("TELEMETRY SIGNALS SUMMARY & STATISTICAL LIMITS").FontSize(11).Bold().FontColor(Colors.Indigo.Darken2);
            col.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(3.2f); // Signal Name
                    columns.RelativeColumn(1.0f); // Unit
                    columns.RelativeColumn(2.0f); // Safe Limits [Min-Max]
                    columns.RelativeColumn(1.4f); // Observed Min
                    columns.RelativeColumn(1.4f); // Observed Max
                    columns.RelativeColumn(1.4f); // Mean Avg
                    columns.RelativeColumn(1.3f); // Total Samples
                    columns.RelativeColumn(1.6f); // Status
                });

                table.Header(header =>
                {
                    header.Cell().Background(Colors.Indigo.Darken3).Padding(4).Text("Signal / Channel").Bold().FontSize(8).FontColor(Colors.White);
                    header.Cell().Background(Colors.Indigo.Darken3).Padding(4).Text("Unit").Bold().FontSize(8).FontColor(Colors.White);
                    header.Cell().Background(Colors.Indigo.Darken3).Padding(4).Text("Safe Limits").Bold().FontSize(8).FontColor(Colors.White);
                    header.Cell().Background(Colors.Indigo.Darken3).Padding(4).Text("Min").Bold().FontSize(8).FontColor(Colors.White);
                    header.Cell().Background(Colors.Indigo.Darken3).Padding(4).Text("Max").Bold().FontSize(8).FontColor(Colors.White);
                    header.Cell().Background(Colors.Indigo.Darken3).Padding(4).Text("Mean").Bold().FontSize(8).FontColor(Colors.White);
                    header.Cell().Background(Colors.Indigo.Darken3).Padding(4).Text("Samples").Bold().FontSize(8).FontColor(Colors.White);
                    header.Cell().Background(Colors.Indigo.Darken3).Padding(4).Text("Status").Bold().FontSize(8).FontColor(Colors.White);
                });

                int idx = 0;
                foreach (var s in data.Signals)
                {
                    var bg = (idx++ % 2 == 0) ? Colors.White : Colors.Grey.Lighten5;
                    table.Cell().Background(bg).Padding(3.5f).Text(s.Name).SemiBold().FontSize(8);
                    table.Cell().Background(bg).Padding(3.5f).Text(s.Unit).FontSize(8);
                    table.Cell().Background(bg).Padding(3.5f).Text($"{s.DesignMin} - {s.DesignMax}").FontSize(8);
                    table.Cell().Background(bg).Padding(3.5f).Text(s.LowestValue.ToString("F1")).FontSize(8);
                    table.Cell().Background(bg).Padding(3.5f).Text(s.PeakValue.ToString("F1")).FontSize(8);
                    table.Cell().Background(bg).Padding(3.5f).Text(s.AvgValue.ToString("F1")).FontSize(8);
                    table.Cell().Background(bg).Padding(3.5f).Text(s.TotalReadings.ToString("N0")).FontSize(8);

                    var statusCell = table.Cell().Background(bg).Padding(3.5f);
                    if (s.HasViolation)
                        statusCell.Text("BREACHED").Bold().FontSize(8).FontColor(Colors.Red.Darken2);
                    else
                        statusCell.Text("NORMAL").Bold().FontSize(8).FontColor(Colors.Green.Darken2);
                }
            });

            // 4. Trend Graphs ("some graphs") on Page 1
            var chartsToShow = data.Signals.Where(s => s.DataPoints.Any()).Take(4).ToList();
            if (chartsToShow.Any())
            {
                col.Item().PaddingTop(12).Text("SIGNAL TREND OVERVIEW (UNAGGREGATED PROFILES)").FontSize(10.5f).Bold().FontColor(Colors.Indigo.Darken2);
                
                for (int i = 0; i < chartsToShow.Count; i += 2)
                {
                    int chartIdx = i;
                    col.Item().PaddingTop(4).Row(row =>
                    {
                        var s1 = chartsToShow[chartIdx];
                        row.RelativeItem().Container()
                            .Border(1).BorderColor(Colors.Grey.Lighten2)
                            .Padding(4)
                            .Column(c =>
                            {
                                c.Item().Text($"{s1.Name} ({s1.Unit}) • Peak: {s1.PeakValue:F1} | Limit: {s1.DesignMax}").Bold().FontSize(7.5f);
                                c.Item().Height(52).Svg(GenerateSvgTrendChart(s1, 240, 52));
                            });

                        if (chartIdx + 1 < chartsToShow.Count)
                        {
                            row.ConstantItem(8);
                            var s2 = chartsToShow[chartIdx + 1];
                            row.RelativeItem().Container()
                                .Border(1).BorderColor(Colors.Grey.Lighten2)
                                .Padding(4)
                                .Column(c =>
                                {
                                    c.Item().Text($"{s2.Name} ({s2.Unit}) • Peak: {s2.PeakValue:F1} | Limit: {s2.DesignMax}").Bold().FontSize(7.5f);
                                    c.Item().Height(52).Svg(GenerateSvgTrendChart(s2, 240, 52));
                                });
                        }
                    });
                }
            }

            // Excursion Events Table (if any)
            if (data.Events.Any())
            {
                col.Item().PaddingTop(12).Text($"EXCURSION EVENTS LOG ({data.Events.Count} Recorded)").FontSize(10.5f).Bold().FontColor(Colors.Indigo.Darken2);
                col.Item().PaddingTop(3).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(3); // Signal
                        columns.RelativeColumn(2); // Type
                        columns.RelativeColumn(3); // Start Time
                        columns.RelativeColumn(2); // Duration
                        columns.RelativeColumn(2); // Peak Value
                        columns.RelativeColumn(2); // Threshold
                    });

                    table.Header(header =>
                    {
                        header.Cell().Background(Colors.Grey.Darken2).Padding(3).Text("Signal").Bold().FontSize(7.5f).FontColor(Colors.White);
                        header.Cell().Background(Colors.Grey.Darken2).Padding(3).Text("Type").Bold().FontSize(7.5f).FontColor(Colors.White);
                        header.Cell().Background(Colors.Grey.Darken2).Padding(3).Text("Start (UTC)").Bold().FontSize(7.5f).FontColor(Colors.White);
                        header.Cell().Background(Colors.Grey.Darken2).Padding(3).Text("Duration").Bold().FontSize(7.5f).FontColor(Colors.White);
                        header.Cell().Background(Colors.Grey.Darken2).Padding(3).Text("Peak").Bold().FontSize(7.5f).FontColor(Colors.White);
                        header.Cell().Background(Colors.Grey.Darken2).Padding(3).Text("Limit").Bold().FontSize(7.5f).FontColor(Colors.White);
                    });

                    foreach (var evt in data.Events.Take(6))
                    {
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(evt.SignalName).FontSize(7.5f);
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(evt.EventType).FontSize(7.5f).Bold().FontColor(Colors.Red.Darken1);
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(evt.StartTime.ToString("yyyy-MM-dd HH:mm")).FontSize(7.5f);
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(3).Text($"{evt.DurationMinutes:F1} m").FontSize(7.5f);
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(evt.PeakValue?.ToString("F1") ?? "-").FontSize(7.5f);
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(evt.Threshold?.ToString("F1") ?? "-").FontSize(7.5f);
                    }
                });
            }

            // Alerts Table (if any)
            if (data.Alerts.Any())
            {
                col.Item().PaddingTop(12).Text($"SYSTEM ALERTS AUDIT ({data.Alerts.Count} Recorded)").FontSize(10.5f).Bold().FontColor(Colors.Indigo.Darken2);
                col.Item().PaddingTop(3).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(3); // Signal
                        columns.RelativeColumn(2); // Severity
                        columns.RelativeColumn(3); // Triggered At
                        columns.RelativeColumn(2); // Trigger Value
                        columns.RelativeColumn(2); // Threshold
                        columns.RelativeColumn(2); // Status
                    });

                    table.Header(header =>
                    {
                        header.Cell().Background(Colors.Grey.Darken2).Padding(3).Text("Signal").Bold().FontSize(7.5f).FontColor(Colors.White);
                        header.Cell().Background(Colors.Grey.Darken2).Padding(3).Text("Severity").Bold().FontSize(7.5f).FontColor(Colors.White);
                        header.Cell().Background(Colors.Grey.Darken2).Padding(3).Text("Triggered At").Bold().FontSize(7.5f).FontColor(Colors.White);
                        header.Cell().Background(Colors.Grey.Darken2).Padding(3).Text("Trigger Val").Bold().FontSize(7.5f).FontColor(Colors.White);
                        header.Cell().Background(Colors.Grey.Darken2).Padding(3).Text("Threshold").Bold().FontSize(7.5f).FontColor(Colors.White);
                        header.Cell().Background(Colors.Grey.Darken2).Padding(3).Text("Status").Bold().FontSize(7.5f).FontColor(Colors.White);
                    });

                    foreach (var al in data.Alerts.Take(6))
                    {
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(al.SignalName).FontSize(7.5f);
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(al.Severity).FontSize(7.5f).Bold().FontColor(al.Severity.Equals("CRITICAL", StringComparison.OrdinalIgnoreCase) ? Colors.Red.Darken2 : Colors.Orange.Darken2);
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(al.TriggeredAt.ToString("yyyy-MM-dd HH:mm")).FontSize(7.5f);
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(al.TriggerValue.ToString("F1")).FontSize(7.5f);
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(al.ThresholdValue.ToString("F1")).FontSize(7.5f);
                        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(al.Status).FontSize(7.5f);
                    }
                });
            }

            // ==========================================
            // PAGE 2+: FULL RAW DATA TABLE OR GRAPHS & TRENDS SUMMARY
            // ==========================================
            if (data.IncludeFullRawData)
            {
                col.Item().PageBreak();

                col.Item().Text("RAW TELEMETRY READINGS LOG (UNAGGREGATED)").FontSize(13).Bold().FontColor(Colors.Indigo.Darken2);
                col.Item().PaddingTop(2).Text("Chronological sequence of unaggregated sensor measurements. Each value is presented exactly as recorded in signal_data.")
                    .FontSize(8.5f).FontColor(Colors.Grey.Darken1);

                foreach (var sig in data.Signals)
                {
                    col.Item().PaddingTop(12).Container()
                        .Background(Colors.Indigo.Lighten5)
                        .Border(1).BorderColor(Colors.Indigo.Lighten3)
                        .Padding(6)
                        .Row(r =>
                        {
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().Text($"Signal: {sig.Name} ({sig.Unit})").Bold().FontSize(10.5f).FontColor(Colors.Indigo.Darken3);
                                c.Item().Text($"Operating Limits: [{sig.DesignMin:F1} to {sig.DesignMax:F1}]  •  Observed: Min {sig.LowestValue:F2}, Max {sig.PeakValue:F2}, Mean {sig.AvgValue:F2}")
                                    .FontSize(8).FontColor(Colors.Grey.Darken2);
                            });
                            r.ConstantItem(240).AlignRight().Text($"All {sig.DataPoints.Count:N0} Raw Readings").Bold().FontSize(9.0f).FontColor(Colors.Indigo.Darken2);
                        });

                    if (sig.DataPoints.Count == 0)
                    {
                        col.Item().PaddingTop(4).Text("No raw readings recorded for this channel in the selected timeframe.").FontSize(8).FontColor(Colors.Grey.Medium);
                        continue;
                    }

                    // Show all unaggregated readings for this signal
                    var pointsToPrint = sig.DataPoints;
                    int totalPoints = pointsToPrint.Count;
                    int rowPairs = (totalPoints + 1) / 2;

                    col.Item().PaddingTop(4).Table(rawTable =>
                    {
                        rawTable.ColumnsDefinition(cd =>
                        {
                            cd.ConstantColumn(22);       // #
                            cd.RelativeColumn(2.6f);     // Timestamp
                            cd.RelativeColumn(1.8f);     // Value
                            cd.RelativeColumn(1.4f);     // Status
                            cd.ConstantColumn(8);        // Spacer
                            cd.ConstantColumn(22);       // #
                            cd.RelativeColumn(2.6f);     // Timestamp
                            cd.RelativeColumn(1.8f);     // Value
                            cd.RelativeColumn(1.4f);     // Status
                        });

                        rawTable.Header(hdr =>
                        {
                            hdr.Cell().Background(Colors.Indigo.Darken3).Padding(3).Text("#").Bold().FontSize(7.5f).FontColor(Colors.White);
                            hdr.Cell().Background(Colors.Indigo.Darken3).Padding(3).Text("Time (UTC)").Bold().FontSize(7.5f).FontColor(Colors.White);
                            hdr.Cell().Background(Colors.Indigo.Darken3).Padding(3).Text($"Value ({sig.Unit})").Bold().FontSize(7.5f).FontColor(Colors.White);
                            hdr.Cell().Background(Colors.Indigo.Darken3).Padding(3).Text("Status").Bold().FontSize(7.5f).FontColor(Colors.White);
                            hdr.Cell().Background(Colors.White); // spacer
                            hdr.Cell().Background(Colors.Indigo.Darken3).Padding(3).Text("#").Bold().FontSize(7.5f).FontColor(Colors.White);
                            hdr.Cell().Background(Colors.Indigo.Darken3).Padding(3).Text("Time (UTC)").Bold().FontSize(7.5f).FontColor(Colors.White);
                            hdr.Cell().Background(Colors.Indigo.Darken3).Padding(3).Text($"Value ({sig.Unit})").Bold().FontSize(7.5f).FontColor(Colors.White);
                            hdr.Cell().Background(Colors.Indigo.Darken3).Padding(3).Text("Status").Bold().FontSize(7.5f).FontColor(Colors.White);
                        });

                        for (int r = 0; r < rowPairs; r++)
                        {
                            var bg = (r % 2 == 0) ? Colors.White : Colors.Grey.Lighten5;

                            // Left item: index = r * 2
                            int leftIdx = r * 2;
                            var pLeft = pointsToPrint[leftIdx];
                            bool isLeftBreach = pLeft.Value > sig.DesignMax || pLeft.Value < sig.DesignMin;

                            rawTable.Cell().Background(bg).Padding(2.5f).Text($"{leftIdx + 1}").FontSize(7).FontColor(Colors.Grey.Medium);
                            rawTable.Cell().Background(bg).Padding(2.5f).Text(pLeft.Time.ToString("yyyy-MM-dd HH:mm")).FontSize(7.5f);
                            rawTable.Cell().Background(bg).Padding(2.5f).Text(pLeft.Value.ToString("F2")).FontSize(7.5f).Bold();

                            var leftStatus = rawTable.Cell().Background(bg).Padding(2.5f);
                            if (isLeftBreach)
                                leftStatus.Text(pLeft.Value > sig.DesignMax ? "HIGH" : "LOW").Bold().FontSize(7).FontColor(Colors.Red.Darken2);
                            else
                                leftStatus.Text("OK").FontSize(7).FontColor(Colors.Green.Darken2);

                            rawTable.Cell().Background(Colors.White); // spacer

                            // Right item: index = r * 2 + 1
                            int rightIdx = r * 2 + 1;
                            if (rightIdx < totalPoints)
                            {
                                var pRight = pointsToPrint[rightIdx];
                                bool isRightBreach = pRight.Value > sig.DesignMax || pRight.Value < sig.DesignMin;

                                rawTable.Cell().Background(bg).Padding(2.5f).Text($"{rightIdx + 1}").FontSize(7).FontColor(Colors.Grey.Medium);
                                rawTable.Cell().Background(bg).Padding(2.5f).Text(pRight.Time.ToString("yyyy-MM-dd HH:mm")).FontSize(7.5f);
                                rawTable.Cell().Background(bg).Padding(2.5f).Text(pRight.Value.ToString("F2")).FontSize(7.5f).Bold();

                                var rightStatus = rawTable.Cell().Background(bg).Padding(2.5f);
                                if (isRightBreach)
                                    rightStatus.Text(pRight.Value > sig.DesignMax ? "HIGH" : "LOW").Bold().FontSize(7).FontColor(Colors.Red.Darken2);
                                else
                                    rightStatus.Text("OK").FontSize(7).FontColor(Colors.Green.Darken2);
                            }
                            else
                            {
                                rawTable.Cell().Background(bg);
                                rawTable.Cell().Background(bg);
                                rawTable.Cell().Background(bg);
                                rawTable.Cell().Background(bg);
                            }
                        }
                    });
                }
            }
            else
            {
                // GRAPHS & TRENDS SUMMARY ONLY MODE
                col.Item().PageBreak();

                col.Item().Text("VISUAL TELEMETRY TRENDS & ANALYSIS DASHBOARD").FontSize(13).Bold().FontColor(Colors.Indigo.Darken2);
                col.Item().PaddingTop(2).Text("Graphical summary mode enabled. Displaying full-resolution trend curves, safe threshold boundaries, and peak statistical envelopes for all selected signals.")
                    .FontSize(8.5f).FontColor(Colors.Grey.Darken1);

                foreach (var sig in data.Signals)
                {
                    col.Item().PaddingTop(10).Container()
                        .Border(1).BorderColor(Colors.Grey.Lighten2)
                        .Padding(8)
                        .Column(c =>
                        {
                            c.Item().Row(r =>
                            {
                                r.RelativeItem().Text($"Signal Trend: {sig.Name} ({sig.Unit})").Bold().FontSize(10.0f).FontColor(Colors.Indigo.Darken3);
                                r.ConstantItem(250).AlignRight().Text($"Design Limit: {sig.DesignMin:F1} - {sig.DesignMax:F1} | Peak: {sig.PeakValue:F1} | Avg: {sig.AvgValue:F1}")
                                    .FontSize(8).FontColor(Colors.Grey.Darken2);
                            });

                            if (sig.DataPoints.Any())
                            {
                                c.Item().PaddingTop(6).Height(75).Svg(GenerateSvgTrendChart(sig, 450, 75));
                            }
                            else
                            {
                                c.Item().PaddingTop(6).Text("No data points available for chart rendering.").FontSize(8).FontColor(Colors.Grey.Medium);
                            }
                        });
                }
            }
        });
    }

    private void ComposeKpiCard(IContainer container, string label, string value, string bgColor, string textColor)
    {
        container.Background(bgColor)
            .Border(1).BorderColor(Colors.Grey.Lighten2)
            .Padding(8)
            .Column(c =>
            {
                c.Item().Text(label).FontSize(8).SemiBold().FontColor(Colors.Grey.Darken2);
                c.Item().PaddingTop(2).Text(value).FontSize(14).Bold().FontColor(textColor);
            });
    }

    private void ComposeFooter(IContainer container)
    {
        container.BorderTop(1).BorderColor(Colors.Grey.Lighten2).PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text("CONFIDENTIAL  •  Generated by Industrial Asset Telemetry Analytics Platform").FontSize(7.5f).FontColor(Colors.Grey.Medium);
            row.RelativeItem().AlignRight().Text(text =>
            {
                text.Span("Page ");
                text.CurrentPageNumber();
                text.Span(" of ");
                text.TotalPages();
            });
        });
    }

    private string GenerateSvgTrendChart(SignalSummaryDto sig, int width = 240, int height = 52)
    {
        try
        {
            int padTop = 4;
            int padBottom = 10;
            int padLeft = 4;
            int padRight = 4;

            var points = sig.DataPoints;
            if (points == null || points.Count == 0)
            {
                return $"<svg viewBox=\"0 0 {width} {height}\" xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"100%\" height=\"100%\" fill=\"#fafafa\" rx=\"3\"/><text x=\"10\" y=\"25\" fill=\"#888\" font-size=\"8\">No telemetry recorded</text></svg>";
            }

            double minObserved = points.Min(p => p.Value);
            double maxObserved = points.Max(p => p.Value);
            double minVal = Math.Min(sig.DesignMin, minObserved);
            double maxVal = Math.Max(sig.DesignMax, maxObserved);
            if (Math.Abs(maxVal - minVal) < 0.001) maxVal += 1.0;

            float chartH = height - padTop - padBottom;
            float chartW = width - padLeft - padRight;

            Func<double, float> getY = val =>
            {
                float norm = (float)((val - minVal) / (maxVal - minVal));
                norm = Math.Clamp(norm, 0.0f, 1.0f);
                return padTop + chartH - (norm * chartH);
            };

            int stride = Math.Max(1, points.Count / 250);
            int totalVisualSteps = ((points.Count - 1) / stride);
            float step = totalVisualSteps > 0 ? chartW / totalVisualSteps : 0;
            var polyPoints = new StringBuilder();
            int stepIdx = 0;
            for (int i = 0; i < points.Count; i += stride)
            {
                float x = padLeft + (stepIdx++ * step);
                float y = getY(points[i].Value);
                polyPoints.Append(CultureInfo.InvariantCulture, $"{x:F1},{y:F1} ");
            }

            // Ensure polyline has at least two points to avoid SVG rendering failure
            if (stepIdx == 1)
            {
                float x2 = padLeft + chartW;
                float y2 = getY(points[0].Value);
                polyPoints.Append(CultureInfo.InvariantCulture, $"{x2:F1},{y2:F1} ");
            }

            float thresholdY = getY(sig.DesignMax);

            var sb = new StringBuilder();
            sb.Append($"<svg viewBox=\"0 0 {width} {height}\" xmlns=\"http://www.w3.org/2000/svg\">");
            sb.Append($"<rect width=\"100%\" height=\"100%\" fill=\"#fafafa\" rx=\"3\"/>");

            // Grid horizontal lines
            sb.Append($"<line x1=\"{padLeft}\" y1=\"{padTop}\" x2=\"{width - padRight}\" y2=\"{padTop}\" stroke=\"#e5e7eb\" stroke-width=\"1\"/>");
            sb.Append($"<line x1=\"{padLeft}\" y1=\"{height - padBottom}\" x2=\"{width - padRight}\" y2=\"{height - padBottom}\" stroke=\"#e5e7eb\" stroke-width=\"1\"/>");

            // Threshold red dashed line
            if (thresholdY >= padTop && thresholdY <= height - padBottom)
            {
                sb.Append($"<line x1=\"{padLeft}\" y1=\"{thresholdY:F1}\" x2=\"{width - padRight}\" y2=\"{thresholdY:F1}\" stroke=\"#ef4444\" stroke-dasharray=\"2,2\" stroke-width=\"1\"/>");
                sb.Append($"<text x=\"{width - 55}\" y=\"{thresholdY - 1:F1}\" font-size=\"6\" fill=\"#dc2626\" font-family=\"Arial\">Limit: {sig.DesignMax}</text>");
            }

            // Signal data line
            sb.Append($"<polyline points=\"{polyPoints}\" fill=\"none\" stroke=\"#4f46e5\" stroke-width=\"1.4\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");

            // Bottom label
            string firstTime = points.First().Time.ToString("MM/dd HH:mm");
            string lastTime = points.Last().Time.ToString("MM/dd HH:mm");
            sb.Append($"<text x=\"{padLeft}\" y=\"{height - 1}\" font-size=\"6\" fill=\"#6b7280\" font-family=\"Arial\">{firstTime}</text>");
            sb.Append($"<text x=\"{width - 60}\" y=\"{height - 1}\" font-size=\"6\" fill=\"#6b7280\" font-family=\"Arial\">{lastTime}</text>");

            sb.Append("</svg>");
            return sb.ToString();
        }
        catch
        {
            return $"<svg viewBox=\"0 0 {width} {height}\" xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"100%\" height=\"100%\" fill=\"#fafafa\" rx=\"3\"/><text x=\"10\" y=\"25\" fill=\"#dc2626\" font-size=\"8\">Chart Error</text></svg>";
        }
    }
}
