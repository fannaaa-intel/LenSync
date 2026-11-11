Option Strict On
Option Explicit On
Option Infer On

Imports System.Globalization
Imports System.Threading.Tasks
Imports LenSync.Core
Imports LenSync.Models
Imports LenSync.Printing
Imports LenSync.UI
Imports LenSync.UI.Controls
Imports LenSync.UI.Dialogs

Namespace Modules.Reports

    ''' <summary>
    ''' Reports > Create report: pick the report and period on the left, see the letter pages exactly as they will print
    ''' on the right, then "Print report". Every print gets a new report no. and verification code. Right before
    ''' printing the report is rebuilt; if the sales changed since the preview, the preview updates and nothing prints.
    ''' </summary>
    Public Class CreateReportPage
        Inherits ModulePage

        Private Shared ReadOnly PH As CultureInfo = CultureInfo.GetCultureInfo("en-PH")
        Private Shared ReadOnly PeriodNames As String() = {"Today", "Yesterday", "This week", "Last 7 days", "This month", "Last month", "This year", "Custom dates"}
        Private Const CustomPeriod As Integer = 7
        Private Const DefaultPeriod As Integer = 4

        Private ReadOnly _type As New LsSelect()
        Private ReadOnly _typeNote As New Label()
        Private ReadOnly _period As New LsSelect()
        Private ReadOnly _from As New LsDatePicker()
        Private ReadOnly _to As New LsDatePicker()
        Private ReadOnly _summary As New Label()
        Private ReadOnly _preview As New PagePreview()
        Private ReadOnly _btnPrev As New LsButton("‹  Previous", ButtonVariant.Ghost)
        Private ReadOnly _btnNext As New LsButton("Next  ›", ButtonVariant.Ghost)
        Private ReadOnly _pageLabel As New Label()
        Private ReadOnly _btnPrint As New LsButton("Print report", ButtonVariant.Primary)
        Private ReadOnly _lblType As Label
        Private ReadOnly _lblPeriod As Label
        Private ReadOnly _lblFrom As Label
        Private ReadOnly _lblTo As Label
        Private ReadOnly _debounce As New Timer() With {.Interval = 220}

        Private _data As ReportData
        Private _printer As ReportPrinter
        Private _seq As Integer
        Private _suppress As Boolean
        Private _loading As Boolean
        Private _printing As Boolean

        Public Sub New()
            MyBase.New("Create report", "Choose a report and period, check the preview, then print it with its verification code.")
            _lblType = AddFieldLabel("Report")
            _lblPeriod = AddFieldLabel("Period")
            _lblFrom = AddFieldLabel("From")
            _lblTo = AddFieldLabel("To")

            _type.FieldName = "Report"
            _type.Icon = LineIcon.Reports
            _type.SetItems(ReportTypes.All.Select(Function(t) ReportTypes.Title(t)))
            _period.FieldName = "Period"
            _period.Icon = LineIcon.Bookings
            _period.SetItems(PeriodNames)
            For Each picker In {_from, _to}
                picker.DateFormat = "MMM d, yyyy"
                picker.ShowIcon = False
            Next
            _from.FieldName = "From date"
            _to.FieldName = "To date"
            _typeNote.Font = Theme.Label
            _typeNote.ForeColor = Theme.Slate
            _typeNote.BackColor = Theme.White
            _typeNote.AutoEllipsis = True
            _summary.Font = Theme.Label
            _summary.ForeColor = Theme.Charcoal
            _summary.BackColor = Theme.White
            _summary.AutoEllipsis = True
            _pageLabel.Font = Theme.LabelBold
            _pageLabel.ForeColor = Theme.Charcoal
            _pageLabel.BackColor = Theme.White
            _pageLabel.TextAlign = ContentAlignment.MiddleCenter
            _btnPrev.AccessibleName = "Previous page"
            _btnNext.AccessibleName = "Next page"
            AddAction(_btnPrint)
            Controls.AddRange({_type, _typeNote, _period, _from, _to, _summary, _preview, _btnPrev, _pageLabel, _btnNext})

            ' Keyboard order: the choices top to bottom, then the preview and its page buttons, then Print.
            Dim order As Control() = {_type, _period, _from, _to, _preview, _btnPrev, _btnNext, Actions}
            For i = 0 To order.Length - 1
                order(i).TabIndex = i
            Next

            AddHandler _type.SelectedIndexChanged, Sub()
                                                       _typeNote.Text = ReportTypes.Description(SelectedType)
                                                       If Not _suppress Then ScheduleBuild()
                                                   End Sub
            AddHandler _period.SelectedIndexChanged, Sub() OnPeriodChanged()
            AddHandler _from.ValueChanged, Sub() OnDateEdited()
            AddHandler _to.ValueChanged, Sub() OnDateEdited()
            AddHandler _debounce.Tick, Async Sub()
                                           _debounce.Stop()
                                           Await BuildAsync(keepPage:=False)
                                       End Sub
            AddHandler _preview.PageChanged, Sub() UpdateUi()
            AddHandler _btnPrev.Click, Sub() _preview.PageIndex -= 1
            AddHandler _btnNext.Click, Sub() _preview.PageIndex += 1
            AddHandler _btnPrint.Click, Async Sub() Await PrintAsync()

            _suppress = True
            _type.SelectedIndex = 0
            _period.SelectedIndex = DefaultPeriod
            ApplyPreset(DefaultPeriod)
            _suppress = False
            _preview.ShowMessage("Preparing the preview...")
            UpdateUi()
        End Sub

        Private ReadOnly Property SelectedType As ReportType
            Get
                Return ReportTypes.All(Math.Max(0, _type.SelectedIndex))
            End Get
        End Property

        ''' <summary>Rebuild on every visit: sales may have changed in Add Service since the last one.</summary>
        Protected Overrides ReadOnly Property RefreshOnShow As Boolean
            Get
                Return True
            End Get
        End Property

        Public Overrides Async Function ReloadAsync() As Task
            _debounce.Stop()
            Await BuildAsync(keepPage:=True)
        End Function

        ''' <summary>Used by History > "Open again": the same report and period (printing gives it a new number).</summary>
        Public Sub Apply(type As ReportType, fromDay As Date, toDay As Date)
            _suppress = True
            Try
                _type.SelectedIndex = Array.IndexOf(ReportTypes.All, type)
                Dim preset = CustomPeriod
                For i = 0 To CustomPeriod - 1
                    Dim r = PresetRange(i, Date.Today)
                    If r.Item1 = fromDay.Date AndAlso r.Item2 = toDay.Date Then
                        preset = i
                        Exit For
                    End If
                Next
                _period.SelectedIndex = preset
                _from.Value = fromDay
                _to.Value = toDay
            Finally
                _suppress = False
            End Try
            Validator.ClearAll(Me)
        End Sub

        ' ---------------------------------------------------------------- period
        ''' <summary>(from, to) for a quick period. Weeks start on Monday.</summary>
        Public Shared Function PresetRange(index As Integer, today As Date) As Tuple(Of Date, Date)
            Dim t = today.Date
            Dim monthStart = New Date(t.Year, t.Month, 1)
            Select Case index
                Case 0 : Return Tuple.Create(t, t)
                Case 1 : Return Tuple.Create(t.AddDays(-1), t.AddDays(-1))
                Case 2 : Return Tuple.Create(t.AddDays(-((CInt(t.DayOfWeek) + 6) Mod 7)), t)
                Case 3 : Return Tuple.Create(t.AddDays(-6), t)
                Case 4 : Return Tuple.Create(monthStart, t)
                Case 5 : Return Tuple.Create(monthStart.AddMonths(-1), monthStart.AddDays(-1))
                Case Else : Return Tuple.Create(New Date(t.Year, 1, 1), t)
            End Select
        End Function

        Private Sub ApplyPreset(index As Integer)
            If index < 0 OrElse index >= CustomPeriod Then Return
            Dim r = PresetRange(index, Date.Today)
            Dim was = _suppress
            _suppress = True
            ' Set in an order that never puts "to" before "from" on the way.
            If r.Item1 > _to.Value Then
                _to.Value = r.Item2
                _from.Value = r.Item1
            Else
                _from.Value = r.Item1
                _to.Value = r.Item2
            End If
            _suppress = was
        End Sub

        Private Sub OnPeriodChanged()
            If _suppress Then Return
            ApplyPreset(_period.SelectedIndex)
            ClearDateErrors()
            ScheduleBuild()
        End Sub

        Private Sub ClearDateErrors()
            Validator.Clear(_from)
            Validator.Clear(_to)
        End Sub

        Private Sub OnDateEdited()
            If _suppress Then Return
            _suppress = True
            _period.SelectedIndex = CustomPeriod
            _suppress = False
            ScheduleBuild()
        End Sub

        Private Sub ScheduleBuild()
            ' Short pause so stepping through dates with Up/Down builds once, not on every day.
            _debounce.Stop()
            _debounce.Start()
            UpdateUi()
        End Sub

        ' ---------------------------------------------------------------- build / preview
        Private Async Function BuildAsync(keepPage As Boolean) As Task
            Dim type = SelectedType
            Dim fromDay = _from.Value
            Dim toDay = _to.Value
            Dim problem = ReportService.RangeProblem(fromDay, toDay)
            If problem IsNot Nothing Then
                _seq += 1
                ' Shown under From: that field starts at the column edge, so the message has the column's full width.
                Validator.SetError(_from, problem)
                ShowNothing("Fix the dates on the left to see the preview.")
                Return
            End If
            ClearDateErrors()
            _seq += 1
            Dim mine = _seq
            _loading = True
            _preview.Busy = True
            UpdateUi()
            Dim data As ReportData = Nothing
            Dim failure As Exception = Nothing
            Try
                data = Await Task.Run(Function() ReportService.Build(type, fromDay, toDay))
            Catch ex As Exception
                failure = ex
            End Try
            If IsDisposed OrElse mine <> _seq Then Return
            _loading = False
            If failure IsNot Nothing Then
                AppLog.Write(failure, "Building report preview")
                ShowNothing("The preview couldn't be loaded. " & Db.FriendlyMessage(failure))
                Return
            End If
            ShowData(data, keepPage AndAlso _data IsNot Nothing AndAlso _data.Type = type AndAlso _data.DateFrom = fromDay AndAlso _data.DateTo = toDay)
        End Function

        Private Sub ShowData(data As ReportData, keepPage As Boolean)
            Dim printer As New ReportPrinter(data)
            _preview.SetDocument(printer.PageCount, ReportPrinter.PageHeightPt / ReportPrinter.PageWidthPt,
                                 Function(index, widthPx) printer.RenderPreview(index, widthPx), keepPage)
            Dim old = _printer
            _printer = printer
            _data = data
            old?.Dispose()
            _loading = False
            UpdateUi()
        End Sub

        Private Sub ShowNothing(message As String)
            _loading = False
            _preview.ShowMessage(message)
            Dim old = _printer
            _printer = Nothing
            _data = Nothing
            old?.Dispose()
            UpdateUi()
        End Sub

        Private Sub UpdateUi()
            Dim count = _preview.PageCount
            _btnPrev.Enabled = count > 0 AndAlso _preview.PageIndex > 0
            _btnNext.Enabled = count > 0 AndAlso _preview.PageIndex < count - 1
            _pageLabel.Text = If(count = 0, "", $"Page {_preview.PageIndex + 1} of {count}")
            _btnPrint.Enabled = _data IsNot Nothing AndAlso Not _loading AndAlso Not _printing AndAlso Not _debounce.Enabled
            If _data Is Nothing Then
                _summary.Text = ""
            Else
                Dim rows = _data.RowCount
                Dim text = $"{count} page{If(count = 1, "", "s")}  ·  {rows:#,##0} row{If(rows = 1, "", "s")}  ·  Net sales {Money.Format(_data.NetSales)}"
                If _data.IsIssued Then
                    text &= vbLf & $"Printed as {_data.ReportNo}, code {ReportService.FormatCode(_data.VerificationCode)}. Print again for a new copy with a new number."
                Else
                    text &= vbLf & "The report no. and verification code are given when you print."
                End If
                _summary.Text = text
            End If
        End Sub

        ' ---------------------------------------------------------------- print
        Private Async Function PrintAsync() As Task
            Dim shown = _data
            If shown Is Nothing OrElse _printing OrElse _loading Then Return
            _printing = True
            UpdateUi()
            Try
                Dim type = shown.Type, fromDay = shown.DateFrom, toDay = shown.DateTo
                ' Rebuild from the records right now: what prints must be what the preview showed.
                Dim fresh = Await RunAsync(Function() ReportService.Build(type, fromDay, toDay), "Preparing report for printing")
                If fresh Is Nothing OrElse IsDisposed Then Return
                If ReportService.ComputeHash(fresh) <> ReportService.ComputeHash(shown) Then
                    ShowData(fresh, keepPage:=True)
                    LsDialog.Info("The sales for this period changed after the preview was made (a sale was saved or voided). " &
                                  "The preview now shows the latest figures. Check it, then click Print report again.", "Preview updated")
                    Return
                End If

                Dim settings = ReportPrinter.ChoosePrinter(FindForm())
                If settings Is Nothing Then Return
                Dim printerName = settings.PrinterName
                Dim issued = Await RunAsync(Function()
                                                ReportService.Issue(fresh, printerName)
                                                Return True
                                            End Function, "Issuing report")
                If Not issued OrElse IsDisposed Then Return
                ShowData(fresh, keepPage:=True)

                Try
                    _printer.Print(settings)
                Catch ex As Exception
                    AppLog.Write(ex, "Printing report " & fresh.ReportNo)
                    LsDialog.Error($"{fresh.ReportNo} was recorded, but the printer reported a problem, so the paper may not have printed. " &
                                   "Check that the printer is on and connected, then click Print report again (the new copy gets a new report no.).",
                                   "Printing failed")
                    Return
                End Try
                Toast.Show($"{fresh.ReportNo} sent to {printerName}. Verification code {ReportService.FormatCode(fresh.VerificationCode)}.", ToastKind.Success)
            Finally
                _printing = False
                If Not IsDisposed Then UpdateUi()
            End Try
        End Function

        ' ---------------------------------------------------------------- layout
        Protected Overrides Sub LayoutContent(area As Rectangle)
            ' From/To sit side by side: each must show the widest date in full (text + padding + chevron).
            Dim gap = Theme.Dpi(12)
            Dim dateW = TextRenderer.MeasureText("May 30, 2026", Theme.Body).Width + Theme.Dpi(12 + 36 + 8)
            Dim colW = Math.Max(dateW * 2 + gap, Math.Min(Theme.Dpi(300), Math.Max(Theme.Dpi(260), area.Width * 38 \ 100)))
            Dim x = area.Left
            Dim y = area.Top
            Dim fieldH = Theme.Dpi(40)
            Dim lblH = Theme.LabelBold.Height + Theme.Dpi(6)

            _lblType.Bounds = New Rectangle(x, y, colW, lblH)
            y += lblH
            _type.Bounds = New Rectangle(x, y, colW, fieldH)
            y += fieldH + Theme.Dpi(8)
            Dim noteH = Math.Min(Theme.Label.Height * 3 + Theme.Dpi(2),
                                 TextRenderer.MeasureText(If(_typeNote.Text, ""), Theme.Label, New Size(colW, Integer.MaxValue), TextFormatFlags.WordBreak).Height + Theme.Dpi(2))
            _typeNote.Bounds = New Rectangle(x, y, colW, Math.Max(Theme.Label.Height * 2, noteH))
            y = _typeNote.Bottom + Theme.Dpi(16)

            _lblPeriod.Bounds = New Rectangle(x, y, colW, lblH)
            y += lblH
            _period.Bounds = New Rectangle(x, y, colW, fieldH)
            y += fieldH + Theme.Dpi(14)

            Dim half = (colW - gap) \ 2
            _lblFrom.Bounds = New Rectangle(x, y, half, lblH)
            _lblTo.Bounds = New Rectangle(x + half + gap, y, colW - half - gap, lblH)
            y += lblH
            _from.Bounds = New Rectangle(x, y, half, fieldH)
            _to.Bounds = New Rectangle(x + half + gap, y, colW - half - gap, fieldH)
            ' Room for the validation message under the dates.
            y += fieldH + Theme.Dpi(30)
            ' Whole lines only: on short screens drop lines rather than show half a line.
            Dim lines = Math.Max(0, Math.Min(4, (area.Bottom - y) \ Theme.Label.Height))
            _summary.Bounds = New Rectangle(x, y, colW, lines * Theme.Label.Height + If(lines > 0, Theme.Dpi(2), 0))
            _summary.Visible = lines > 0

            ' Preview on the right with the page buttons under it.
            Dim px = x + colW + Theme.Dpi(28)
            Dim pw = Math.Max(0, area.Right - px)
            Dim navH = Theme.Dpi(Theme.ButtonHeight)
            _preview.Bounds = New Rectangle(px, area.Top, pw, Math.Max(0, area.Height - navH - Theme.Dpi(10)))
            Dim ny = _preview.Bottom + Theme.Dpi(10)
            Dim labelW = Theme.Dpi(110)
            Dim cx = px + pw \ 2
            _pageLabel.Bounds = New Rectangle(cx - labelW \ 2, ny, labelW, navH)
            _btnPrev.Location = New Point(_pageLabel.Left - _btnPrev.Width - Theme.Dpi(6), ny)
            _btnNext.Location = New Point(_pageLabel.Right + Theme.Dpi(6), ny)
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then
                _debounce.Dispose()
                _printer?.Dispose()
            End If
            MyBase.Dispose(disposing)
        End Sub

    End Class

End Namespace
