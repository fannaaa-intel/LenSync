Option Strict On
Option Explicit On
Option Infer On

Imports System.Globalization
Imports System.Threading.Tasks
Imports System.Windows.Forms.DataVisualization.Charting
Imports LenSync.Core
Imports LenSync.Models
Imports LenSync.UI
Imports LenSync.UI.Controls
Imports LenSync.UI.Dialogs

Namespace Modules

    ''' <summary>
    ''' Dashboard: four headline tiles (sales today, receipts today, shoots today, to bill), the last 7 days of sales,
    ''' recent receipts and today's schedule. Refreshes after any sale/booking change (AppEvents.SalesChanged) and every
    ''' minute. Users with Add Service can click through to the related tab or receipt; others see it read-only.
    ''' </summary>
    Public Class DashboardView
        Inherits UserControl

        Private Shared ReadOnly PH As CultureInfo = CultureInfo.GetCultureInfo("en-PH")

        Private ReadOnly _tileSales As New StatTile() With {.Text = "Sales today", .IsMoney = True}
        Private ReadOnly _tileReceipts As New StatTile() With {.Text = "Receipts today"}
        Private ReadOnly _tileShoots As New StatTile() With {.Text = "Shoots today"}
        Private ReadOnly _tileToBill As New StatTile() With {.Text = "To bill"}
        Private ReadOnly _chart As Chart = ChartStyle.CreateColumnChart()
        Private ReadOnly _recent As New LsGrid()
        Private ReadOnly _schedule As New LsGrid()
        Private ReadOnly _timer As New Timer() With {.Interval = 60000}
        Private ReadOnly _canOpen As Boolean = Session.HasPermission(Permissions.AddService)

        Private _snap As DashboardSnapshot
        Private _status As String = "Loading..."
        Private _refreshing As Boolean
        Private _again As Boolean
        Private _chartCard As Rectangle
        Private _recentCard As Rectangle
        Private _scheduleCard As Rectangle

        Public Sub New()
            AutoScaleMode = AutoScaleMode.None
            BackColor = Theme.Snow
            DoubleBuffered = True
            SetStyle(ControlStyles.ResizeRedraw, True)

            Dim tabs = {Tuple.Create(_tileSales, 2), Tuple.Create(_tileReceipts, 2), Tuple.Create(_tileShoots, 0), Tuple.Create(_tileToBill, 1)}
            For Each t In tabs
                Dim tab = t.Item2
                t.Item1.Clickable = _canOpen
                AddHandler t.Item1.Click, Sub() OpenAddService(tab)
                Controls.Add(t.Item1)
            Next

            _recent.ColumnHeadersVisible = False
            _recent.RowTemplate.Height = Theme.Dpi(38)
            _recent.SolidStatuses.Clear()
            _recent.SolidStatuses.Add("Completed")
            _recent.AddTextColumn("receipt", "Receipt no.", 27)
            _recent.AddTextColumn("customer", "Customer", 35)
            _recent.AddTextColumn("amount", "Amount", 20, alignRight:=True)
            _recent.AddStatusColumn("status", "Status", 18)
            _recent.EmptyText = "No receipts yet."
            _recent.AccessibleName = "Recent transactions"

            _schedule.ColumnHeadersVisible = False
            _schedule.RowTemplate.Height = Theme.Dpi(38)
            _schedule.SolidStatuses.Clear()
            _schedule.SolidStatuses.Add(BookingStatus.Unpaid)
            _schedule.AddTextColumn("time", "Time", 32)
            _schedule.AddTextColumn("customer", "Customer", 40)
            _schedule.AddStatusColumn("status", "Status", 28)
            _schedule.EmptyText = "No shoots scheduled today."
            _schedule.AccessibleName = "Today's schedule"

            _chart.AccessibleName = "Sales for the last 7 days"
            Controls.AddRange({_chart, _recent, _schedule})

            AddHandler _recent.CellDoubleClick, Sub(s, e) If e.RowIndex >= 0 Then ViewSelectedReceipt()
            AddHandler _recent.KeyDown, Sub(s, e)
                                            If e.KeyCode = Keys.Enter Then
                                                e.Handled = True
                                                ViewSelectedReceipt()
                                            End If
                                        End Sub
            AddHandler _schedule.CellDoubleClick, Sub(s, e) If e.RowIndex >= 0 Then OpenAddService(0)
            AddHandler _timer.Tick, Sub() RefreshNow()
            AddHandler AppEvents.SalesChanged, AddressOf OnSalesChanged
        End Sub

        Protected Overrides Sub OnLoad(e As EventArgs)
            MyBase.OnLoad(e)
            RefreshNow()
            _timer.Start()
        End Sub

        Private Sub OnSalesChanged(sender As Object, e As EventArgs)
            If Not IsDisposed Then RefreshNow()
        End Sub

        ' ---------------------------------------------------------------- data
        ''' <summary>Loads off the UI thread. Overlapping requests are merged into one extra reload.</summary>
        Private Async Sub RefreshNow()
            If _refreshing Then
                _again = True
                Return
            End If
            _refreshing = True
            Try
                Dim snap = Await Task.Run(Function() DashboardService.Load())
                If IsDisposed Then Return
                Apply(snap)
                _status = $"Updated {snap.LoadedAt.ToString("h:mm tt", PH)}  ·  refreshes every minute"
            Catch ex As Exception
                AppLog.Write(ex, "Loading the dashboard")
                If IsDisposed Then Return
                If _snap Is Nothing Then
                    _status = "Couldn't load the dashboard."
                    LsDialog.Error(Db.FriendlyMessage(ex))
                Else
                    ' Keep the last numbers; a dialog every minute would be worse than a quiet note.
                    _status = $"Couldn't refresh at {DateTime.Now.ToString("h:mm tt", PH)}. Trying again in a minute."
                End If
            Finally
                _refreshing = False
            End Try
            Invalidate()
            If _again AndAlso Not IsDisposed Then
                _again = False
                RefreshNow()
            End If
        End Sub

        Private Sub Apply(snap As DashboardSnapshot)
            _snap = snap
            _tileSales.SetValue(snap.Today.Total)
            _tileSales.Note = "Yesterday " & Money.Format(snap.Yesterday.Total)
            _tileReceipts.SetValue(snap.Today.Receipts)
            _tileReceipts.Note = If(snap.VoidedToday > 0, $"{snap.VoidedToday} voided, not counted", "Completed receipts")
            _tileShoots.SetValue(snap.Schedule.Count)
            _tileShoots.Note = If(snap.Schedule.Count = 0, "Nothing scheduled",
                                  If(snap.ShootsPending > 0, $"{snap.ShootsPending} still to shoot", "All shoots done"))
            _tileToBill.SetValue(snap.ToBill)
            _tileToBill.Note = If(snap.ToBill = 0, "Every finished shoot is paid", "Finished shoots awaiting payment")

            _chart.Series(ChartStyle.SeriesName).Points.Clear()
            For Each d In snap.Week
                Dim isToday = d.Day = snap.LoadedAt.Date
                Dim label = If(isToday, "Today", d.Day.ToString("ddd d", PH))
                Dim tip = $"{d.Day.ToString("ddd, MMM d", PH)}: {Money.Format(d.Total)} from {d.Receipts} receipt{If(d.Receipts = 1, "", "s")}"
                ChartStyle.AddColumn(_chart, label, d.Total, tip, highlight:=isToday)
            Next
            ChartStyle.FinishAxis(_chart)

            _recent.Rows.Clear()
            For Each s In snap.Recent
                Dim i = _recent.Rows.Add(s.ReceiptNo, s.CustomerName, Money.Format(s.Total), s.Status)
                _recent.Rows(i).Tag = s
                _recent.Rows(i).Cells("receipt").ToolTipText = $"{AddService.Display.Stamp(s.CreatedAt)} · {s.PaymentMethod} · {s.CashierName}"
            Next
            _recent.ClearSelection()

            _schedule.Rows.Clear()
            For Each b In snap.Schedule
                Dim i = _schedule.Rows.Add(b.ScheduleAt.ToString("h:mm tt", PH), b.CustomerName, b.Status)
                _schedule.Rows(i).Tag = b
                _schedule.Rows(i).Cells("customer").ToolTipText = $"{b.BookingNo} · {b.PackageName}"
            Next
            _schedule.ClearSelection()
            Invalidate()
        End Sub

        ' ---------------------------------------------------------------- actions
        Private Sub OpenAddService(tab As Integer)
            If Not _canOpen Then Return
            TryCast(FindForm(), IModuleHost)?.OpenModule(Permissions.AddService, tab)
        End Sub

        Private Async Sub ViewSelectedReceipt()
            If Not _canOpen Then Return
            Dim sale = TryCast(_recent.SelectedItem, SaleSummary)
            If sale Is Nothing Then Return
            Dim receipt As Receipt
            UseWaitCursor = True
            Try
                receipt = Await Task.Run(Function() Billing.LoadReceipt(sale.Id))
            Catch ex As Exception
                AppLog.Write(ex, "Opening receipt from the dashboard")
                LsDialog.Error(Db.FriendlyMessage(ex))
                Return
            Finally
                UseWaitCursor = False
            End Try
            If receipt Is Nothing Then Return
            Using dlg As New ReceiptDialog(receipt, afterSale:=False, changeAmount:=0D)
                dlg.ShowCard()
            End Using
            RefreshNow()
        End Sub

        ' ---------------------------------------------------------------- layout & paint
        Private ReadOnly Property CardHeader As Integer
            Get
                Return Theme.Dpi(48)
            End Get
        End Property

        Private Function ContentOf(card As Rectangle) As Rectangle
            Dim pad = Theme.Dpi(16)
            Return New Rectangle(card.X + pad, card.Y + CardHeader, Math.Max(0, card.Width - pad * 2), Math.Max(0, card.Height - CardHeader - Theme.Dpi(12)))
        End Function

        Protected Overrides Sub OnLayout(e As LayoutEventArgs)
            MyBase.OnLayout(e)
            If _schedule Is Nothing Then Return
            Dim m = Theme.Dpi(32)
            Dim gap = Theme.Dpi(16)
            Dim top = Theme.Dpi(30)
            Dim tileH = Theme.Dpi(112)
            Dim tileW = Math.Max(0, (Width - m * 2 - gap * 3) \ 4)
            Dim x = m
            For Each t In {_tileSales, _tileReceipts, _tileShoots, _tileToBill}
                t.Bounds = New Rectangle(x, top, tileW, tileH)
                x += tileW + gap
            Next
            Dim cardsTop = top + tileH + gap
            Dim availH = Math.Max(Theme.Dpi(220), Height - cardsTop - Theme.Dpi(24))
            Dim leftW = CInt((Width - m * 2 - gap) * 0.6)
            Dim chartH = CInt((availH - gap) * 0.52)
            _chartCard = New Rectangle(m, cardsTop, leftW, chartH)
            _recentCard = New Rectangle(m, cardsTop + chartH + gap, leftW, availH - chartH - gap)
            _scheduleCard = New Rectangle(m + leftW + gap, cardsTop, Width - m * 2 - leftW - gap, availH)
            _chart.Bounds = ContentOf(_chartCard)
            _recent.Bounds = ContentOf(_recentCard)
            _schedule.Bounds = ContentOf(_scheduleCard)
            Invalidate()
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            MyBase.OnPaint(e)
            Dim g = e.Graphics
            Gfx.HighQuality(g)
            Dim m = Theme.Dpi(32)
            TextRenderer.DrawText(g, _status, Theme.Hint, New Rectangle(m, Theme.Dpi(6), Width - m * 2, Theme.Dpi(20)), Theme.Slate,
                                  TextFormatFlags.Right Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine)
            Dim weekTotal = If(_snap Is Nothing, 0D, _snap.Week.Sum(Function(d) d.Total))
            DrawCard(g, _chartCard, "Sales, last 7 days", If(_snap Is Nothing, "", Money.Format(weekTotal) & " in 7 days"))
            DrawCard(g, _recentCard, "Recent transactions", If(_canOpen, "Double-click to view", ""))
            DrawCard(g, _scheduleCard, "Today's schedule", DateTime.Today.ToString("ddd, MMM d", PH))
        End Sub

        Private Sub DrawCard(g As Graphics, r As Rectangle, title As String, hint As String)
            If r.Width <= 0 OrElse r.Height <= 0 Then Return
            Using path = Gfx.RoundedRect(New RectangleF(r.X + 0.5F, r.Y + 0.5F, r.Width - 1.0F, r.Height - 1.0F), Theme.Dpi(CSng(Theme.Radius)))
                Using b As New SolidBrush(Theme.White)
                    g.FillPath(b, path)
                End Using
                Using p As New Pen(Theme.Mist)
                    g.DrawPath(p, path)
                End Using
            End Using
            Dim pad = Theme.Dpi(18)
            Dim headerRect As New Rectangle(r.X + pad, r.Y + Theme.Dpi(4), r.Width - pad * 2, CardHeader - Theme.Dpi(8))
            Const flags As TextFormatFlags = TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine Or TextFormatFlags.EndEllipsis
            Dim hintW = If(hint.Length = 0, 0, TextRenderer.MeasureText(g, hint, Theme.Label).Width)
            TextRenderer.DrawText(g, title, Theme.BodyBold, New Rectangle(headerRect.X, headerRect.Y, headerRect.Width - hintW - Theme.Dpi(8), headerRect.Height), Theme.Ink, flags)
            If hintW > 0 Then
                TextRenderer.DrawText(g, hint, Theme.Label, headerRect, Theme.Slate, flags Or TextFormatFlags.Right)
            End If
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then
                RemoveHandler AppEvents.SalesChanged, AddressOf OnSalesChanged
                _timer.Stop()
                _timer.Dispose()
            End If
            MyBase.Dispose(disposing)
        End Sub

    End Class

End Namespace
