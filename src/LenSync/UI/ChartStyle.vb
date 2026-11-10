Option Strict On
Option Explicit On
Option Infer On

Imports System.Windows.Forms.DataVisualization.Charting

Namespace UI

    ''' <summary>
    ''' Monochrome charts (System.Windows.Forms.DataVisualization): white background, dotted mist gridlines, slate
    ''' labels, gray columns with the highlighted point in ink. Used by the Dashboard and the reports.
    ''' </summary>
    Public NotInheritable Class ChartStyle

        Private Sub New()
        End Sub

        Public Const AreaName As String = "main"
        Public Const SeriesName As String = "values"

        ''' <summary>A column chart with one series, styled for LENSYNC. Fill it with AddColumn.</summary>
        Public Shared Function CreateColumnChart() As Chart
            Dim c As New Chart() With {
                .BackColor = Theme.White, .AntiAliasing = AntiAliasingStyles.All,
                .TextAntiAliasingQuality = TextAntiAliasingQuality.High, .BorderlineWidth = 0}
            Dim area As New ChartArea(AreaName) With {.BackColor = Theme.White}
            area.Position.Auto = True
            With area.AxisX
                .MajorGrid.Enabled = False
                .MajorTickMark.Enabled = False
                .LineColor = Theme.Mist
                .Interval = 1
                .LabelStyle.Font = Theme.Label
                .LabelStyle.ForeColor = Theme.Slate
                .IsMarginVisible = True
            End With
            With area.AxisY
                .Minimum = 0
                .LineWidth = 0
                .MajorTickMark.Enabled = False
                .MajorGrid.LineColor = Theme.Mist
                .MajorGrid.LineDashStyle = ChartDashStyle.Dot
                .LabelStyle.Font = Theme.Hint
                .LabelStyle.ForeColor = Theme.Slate
                .LabelStyle.Format = "₱#,##0"
            End With
            c.ChartAreas.Add(area)
            Dim s As New Series(SeriesName) With {.ChartType = SeriesChartType.Column, .ChartArea = AreaName, .Color = Theme.Silver,
                                                  .Font = Theme.LabelBold, .LabelForeColor = Theme.Ink}
            s("PointWidth") = "0.55"
            c.Series.Add(s)
            Return c
        End Function

        ''' <summary>Adds a column. <paramref name="highlight"/>: ink fill and the value printed above it.</summary>
        Public Shared Sub AddColumn(chart As Chart, label As String, value As Decimal, tooltip As String, Optional highlight As Boolean = False)
            Dim s = chart.Series(SeriesName)
            Dim i = s.Points.AddXY(label, CDbl(value))
            Dim p = s.Points(i)
            p.ToolTip = tooltip
            If highlight Then
                p.Color = Theme.Ink
                p.Label = "₱" & Decimal.Round(value, 0).ToString("#,##0", Globalization.CultureInfo.InvariantCulture)
            End If
        End Sub

        ''' <summary>
        ''' Sets a round axis maximum with headroom above the tallest column (so its value label sits above it), and
        ''' keeps an all-zero chart from collapsing.
        ''' </summary>
        Public Shared Sub FinishAxis(chart As Chart)
            Dim area = chart.ChartAreas(AreaName)
            Dim max = If(chart.Series(SeriesName).Points.Count = 0, 0, chart.Series(SeriesName).Points.Max(Function(p) p.YValues(0)))
            area.AxisY.Maximum = NiceCeiling(Math.Max(1000, max * 1.25))
            area.AxisY.Interval = area.AxisY.Maximum / 4
        End Sub

        ''' <summary>Rounds up to 1, 2, 2.5 or 5 times a power of ten (e.g. 4,285 -> 5,000).</summary>
        Private Shared Function NiceCeiling(v As Double) As Double
            Dim magnitude = Math.Pow(10, Math.Floor(Math.Log10(v)))
            For Each f In {1.0, 2.0, 2.5, 5.0, 10.0}
                If f * magnitude >= v Then Return f * magnitude
            Next
            Return 10 * magnitude
        End Function

    End Class

End Namespace
