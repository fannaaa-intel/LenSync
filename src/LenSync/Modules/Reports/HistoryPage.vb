Option Strict On
Option Explicit On
Option Infer On

Imports System.Globalization
Imports System.Threading.Tasks
Imports LenSync.Core
Imports LenSync.Models
Imports LenSync.UI
Imports LenSync.UI.Controls

Namespace Modules.Reports

    ''' <summary>
    ''' Reports > History: every printed report (report_logs), newest first. "Open again" loads the same report and
    ''' period on the Create tab; printing it there gives a new report no. (old numbers are never reprinted).
    ''' Verification codes are not listed: they are the proof printed on paper.
    ''' </summary>
    Public Class HistoryPage
        Inherits ModulePage

        Private Shared ReadOnly PH As CultureInfo = CultureInfo.GetCultureInfo("en-PH")
        Private ReadOnly _openAgain As Action(Of ReportLog)
        Private ReadOnly _search As New LsTextBox()
        Private ReadOnly _grid As New LsGrid()
        Private ReadOnly _btnOpen As New LsButton("Open again", ButtonVariant.Secondary)
        Private ReadOnly _lblCount As New Label()
        Private _logs As New List(Of ReportLog)

        Public Sub New(openAgain As Action(Of ReportLog))
            MyBase.New("History", "Reports printed from LENSYNC. Open one again to print the same report with a new number.")
            _openAgain = openAgain
            _search.Placeholder = "Search report no., report or name"
            _search.FieldName = "Search printed reports"
            _search.Icon = LineIcon.Search
            ' Weights fit "Sales by payment method" and "Nov 11, 2026 12:45 PM" at a 1080p/150% content width.
            _grid.AddTextColumn("no", "Report no.", 15)
            _grid.AddTextColumn("type", "Report", 18)
            _grid.AddTextColumn("period", "Period", 15)
            _grid.AddTextColumn("net", "Net sales", 12, alignRight:=True)
            _grid.AddTextColumn("by", "Printed by", 12)
            _grid.AddTextColumn("printer", "Printer", 15)
            _grid.AddTextColumn("at", "Printed", 20)
            _lblCount.Font = Theme.Label
            _lblCount.ForeColor = Theme.Slate
            _lblCount.BackColor = Theme.White
            _lblCount.TextAlign = ContentAlignment.MiddleLeft
            Controls.AddRange({_search, _btnOpen, _grid, _lblCount})

            AddHandler _search.TextChanged, Sub() Fill()
            AddHandler _grid.SelectionChanged, Sub() UpdateButtons()
            AddHandler _grid.CellDoubleClick, Sub(s, e) If e.RowIndex >= 0 Then OpenSelected()
            AddHandler _grid.KeyDown, Sub(s, e)
                                          If e.KeyCode = Keys.Enter Then
                                              e.Handled = True
                                              OpenSelected()
                                          End If
                                      End Sub
            AddHandler _btnOpen.Click, Sub() OpenSelected()
            UpdateButtons()
        End Sub

        ''' <summary>Reload on every visit so a report printed on the Create tab shows up at once.</summary>
        Protected Overrides ReadOnly Property RefreshOnShow As Boolean
            Get
                Return True
            End Get
        End Property

        Public Overrides Async Function ReloadAsync() As Task
            Dim data = Await RunAsync(Function() ReportService.History(), "Loading report history")
            If data Is Nothing Then Return
            _logs = data
            Fill()
        End Function

        ''' <summary>Compact period for the list: "Nov 11, 2026", "Nov 1–11, 2026", "Oct 28 – Nov 3, 2026".</summary>
        Public Shared Function ShortPeriod(fromDay As Date, toDay As Date) As String
            If fromDay.Date = toDay.Date Then Return fromDay.ToString("MMM d, yyyy", PH)
            If fromDay.Year = toDay.Year AndAlso fromDay.Month = toDay.Month Then
                Return $"{fromDay.ToString("MMM d", PH)}–{toDay.Day}, {toDay.Year}"
            End If
            If fromDay.Year = toDay.Year Then Return $"{fromDay.ToString("MMM d", PH)} – {toDay.ToString("MMM d, yyyy", PH)}"
            Return $"{fromDay.ToString("MMM d, yyyy", PH)} – {toDay.ToString("MMM d, yyyy", PH)}"
        End Function

        Private Sub Fill()
            Dim keepId = If(TryCast(_grid.SelectedItem, ReportLog)?.Id, 0)
            Dim q = _search.Text.Trim()
            _grid.Rows.Clear()
            Dim shown = 0
            For Each r In _logs
                If q.Length > 0 AndAlso Not (Has(r.ReportNo, q) OrElse Has(r.TypeTitle, q) OrElse Has(r.GeneratedBy, q) OrElse Has(r.PrinterName, q)) Then Continue For
                Dim i = _grid.Rows.Add(r.ReportNo, r.TypeTitle, ShortPeriod(r.DateFrom, r.DateTo), Money.Format(r.TotalAmount), r.GeneratedBy,
                                       If(r.PrinterName.Length > 0, r.PrinterName, "—"), r.GeneratedAt.ToString("MMM d, yyyy h:mm tt", PH))
                _grid.Rows(i).Tag = r
                shown += 1
            Next
            If keepId <> 0 Then _grid.SelectWhere(Function(t) DirectCast(t, ReportLog).Id = keepId)
            _grid.EmptyText = If(q.Length > 0, "No printed reports match your search.",
                                 "No reports printed yet. Create one on the Create report tab and click Print report.")
            _lblCount.Text = If(_logs.Count = 0, "", If(q.Length > 0, $"{shown} of {_logs.Count} printed reports", $"{_logs.Count} printed report{If(_logs.Count = 1, "", "s")}"))
            UpdateButtons()
        End Sub

        Private Shared Function Has(text As String, q As String) As Boolean
            Return If(text, "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
        End Function

        Private Sub UpdateButtons()
            Dim log = TryCast(_grid.SelectedItem, ReportLog)
            Dim t As ReportType
            _btnOpen.Enabled = log IsNot Nothing AndAlso ReportTypes.FromKey(log.TypeKey, t)
        End Sub

        Private Sub OpenSelected()
            Dim log = TryCast(_grid.SelectedItem, ReportLog)
            Dim t As ReportType
            If log Is Nothing OrElse Not ReportTypes.FromKey(log.TypeKey, t) Then Return
            _openAgain(log)
        End Sub

        Protected Overrides Sub LayoutContent(area As Rectangle)
            Dim bottom = LayoutToolbar(area, {_search}, {_btnOpen}, 160, 360)
            Dim gridTop = bottom + Theme.Dpi(16)
            Dim footH = Theme.Label.Height + Theme.Dpi(12)
            _grid.Bounds = New Rectangle(area.Left, gridTop, area.Width, Math.Max(0, area.Bottom - gridTop - footH))
            _lblCount.Bounds = New Rectangle(area.Left, _grid.Bottom + Theme.Dpi(4), area.Width, footH - Theme.Dpi(4))
            ' Narrow screens: keep what identifies the report, drop the printer, then who printed it.
            _grid.Columns("printer").Visible = area.Width >= Theme.Dpi(980)
            _grid.Columns("by").Visible = area.Width >= Theme.Dpi(800)
        End Sub

    End Class

End Namespace
