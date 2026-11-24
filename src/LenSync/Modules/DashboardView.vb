Option Strict On
Option Explicit On
Option Infer On

Imports System.Globalization
Imports System.Threading.Tasks
Imports LenSync.Core
Imports LenSync.Models
Imports LenSync.UI
Imports LenSync.UI.Controls
Imports LenSync.UI.Dialogs

Namespace Modules

    ''' <summary>
    ''' Dashboard: three headline tiles (today's revenue, shoots today, awaiting payment), the sales chart with a
    ''' Daily / Weekly / Monthly filter, recent receipts and today's schedule. Refreshes after any sale/booking change
    ''' (AppEvents.SalesChanged) and every minute. Users with Add Service can click through to the related tab or
    ''' receipt; others see it read-only.
    ''' </summary>
    Public Class DashboardView
        Inherits UserControl

        Private Shared ReadOnly PH As CultureInfo = CultureInfo.GetCultureInfo("en-PH")
        Private Shared ReadOnly Periods As SalesPeriod() = {SalesPeriod.Daily, SalesPeriod.Weekly, SalesPeriod.Monthly}

        ' "Revenue": the total of the receipts issued today (downpayment included), voided ones never counted.
        Private ReadOnly _tileSales As New StatTile() With {.Text = "Today's revenue", .IsMoney = True}
        Private ReadOnly _tileShoots As New StatTile() With {.Text = "Shoots today"}
        Private ReadOnly _tileAwaiting As New StatTile() With {.Text = "Awaiting payment"}
        Private ReadOnly _chart As New SalesChart()
        Private ReadOnly _period As New LsSelect()
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

            Dim tabs = {Tuple.Create(_tileSales, 2), Tuple.Create(_tileShoots, 0), Tuple.Create(_tileAwaiting, 1)}
            For Each t In tabs
                Dim tab = t.Item2
                t.Item1.Clickable = _canOpen
                AddHandler t.Item1.Click, Sub() OpenAddService(tab)
                Controls.Add(t.Item1)
            Next

            _recent.ColumnHeadersVisible = False
            _recent.RowTemplate.Height = Theme.Dpi(38)
            ' No status column: receipts are final once paid (voiding happens in Billing), so it would always say
            ' Completed. Receipts voided under the old rule show "Voided" in place of the amount.
            ' "AR-2026-000123" must stay whole at the smallest window; the customer name gives way first.
            _recent.AddTextColumn("receipt", "Receipt no.", 42)
            _recent.AddTextColumn("customer", "Customer", 32)
            _recent.AddTextColumn("amount", "Amount", 26, alignRight:=True)
            _recent.EmptyText = "No receipts yet."
            _recent.AccessibleName = "Recent transactions"

            _schedule.ColumnHeadersVisible = False
            _schedule.RowTemplate.Height = Theme.Dpi(38)
            _schedule.SolidStatuses.Clear()
            _schedule.SolidStatuses.Add(BookingStatus.Unpaid)
            ' Time and the status pill need little room; give the customer's name the rest.
            _schedule.AddTextColumn("time", "Time", 26)
            _schedule.AddTextColumn("customer", "Customer", 48)
            _schedule.AddStatusColumn("status", "Status", 26)
            _schedule.EmptyText = "No shoots scheduled today."
            _schedule.AccessibleName = "Today's schedule"

            _chart.AccessibleName = "Sales chart"
            _chart.Clickable = _canOpen
            _period.FieldName = "Show sales by"
            _period.SetItems({"Daily", "Weekly", "Monthly"})
            _period.SelectedIndex = 0
            _period.BackColor = Theme.White   ' it sits on the white chart card, not the snow page
            Controls.AddRange({_period, _chart, _recent, _schedule})

            AddHandler _recent.CellDoubleClick, Sub(s, e) If e.RowIndex >= 0 Then ViewSelectedReceipt()
            AddHandler _recent.KeyDown, Sub(s, e)
                                            If e.KeyCode = Keys.Enter Then
                                                e.Handled = True
                                                ViewSelectedReceipt()
                                            End If
                                        End Sub
            AddHandler _schedule.CellDoubleClick, Sub(s, e) If e.RowIndex >= 0 Then OpenAddService(0)
            AddHandler _chart.BarClicked, Sub() OpenAddService(2)
            AddHandler _period.SelectedIndexChanged, Sub()
                                                         Invalidate()
                                                         RefreshNow()
                                                     End Sub
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
                Dim period = SelectedPeriod
                Dim snap = Await Task.Run(Function() DashboardService.Load(period))
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

        Private ReadOnly Property SelectedPeriod As SalesPeriod
            Get
                Return Periods(Math.Max(0, _period.SelectedIndex))
            End Get
        End Property

        Private Sub Apply(snap As DashboardSnapshot)
            _snap = snap
            _tileSales.SetValue(snap.Today.Total)
            _tileSales.Note = "Yesterday " & Money.Format(snap.Yesterday.Total)
            _tileShoots.SetValue(snap.Schedule.Count)
            _tileShoots.Note = If(snap.Schedule.Count = 0, "Nothing scheduled",
                                  If(snap.ShootsPending > 0, $"{snap.ShootsPending} still to shoot", "All shoots done"))
            _tileAwaiting.SetValue(snap.AwaitingPayment)
            _tileAwaiting.Note = If(snap.AwaitingPayment = 0, "Every finished shoot is paid", "Finished shoots not yet paid")

            _chart.SetBuckets(snap.Buckets, snap.Period, snap.LoadedAt.Date)

            _recent.Rows.Clear()
            For Each s In snap.Recent
                Dim voided = s.Status <> "Completed"
                Dim i = _recent.Rows.Add(s.ReceiptNo, s.CustomerName, If(voided, s.Status, Money.Format(s.Total)))
                _recent.Rows(i).Tag = s
                _recent.Rows(i).Cells("receipt").ToolTipText = $"{AddService.Display.Stamp(s.CreatedAt)} · {s.PaymentMethod} · {s.CashierName}"
                If voided Then
                    _recent.Rows(i).DefaultCellStyle.ForeColor = Theme.Silver
                    _recent.Rows(i).Cells("amount").ToolTipText = $"{Money.Format(s.Total)} (not counted)"
                End If
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
            Dim tileW = Math.Max(0, (Width - m * 2 - gap * 2) \ 3)
            Dim x = m
            For Each t In {_tileSales, _tileShoots, _tileAwaiting}
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
            ' The period filter sits in the chart card's header, at the right.
            Dim selW = Theme.Dpi(140)
            Dim selH = Theme.Dpi(36)
            _period.Bounds = New Rectangle(_chartCard.Right - Theme.Dpi(12) - selW, _chartCard.Y + (CardHeader - selH) \ 2, selW, selH)
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
            Dim span = If(_snap Is Nothing, "", If(_snap.Period = SalesPeriod.Weekly, "8 weeks", If(_snap.Period = SalesPeriod.Monthly, "6 months", "7 days")))
            Dim chartTotal = If(_snap Is Nothing, 0D, _snap.Buckets.Sum(Function(b) b.Total))
            Dim chartTitle = If(SelectedPeriod = SalesPeriod.Weekly, "Weekly sales", If(SelectedPeriod = SalesPeriod.Monthly, "Monthly sales", "Daily sales"))
            DrawCard(g, _chartCard, chartTitle, If(_snap Is Nothing, "", $"{Money.Format(chartTotal)} in {span}"), _period.Width + Theme.Dpi(12))
            DrawCard(g, _recentCard, "Recent transactions", If(_canOpen, "Double-click to view", ""))
            DrawCard(g, _scheduleCard, "Today's schedule", DateTime.Today.ToString("ddd, MMM d", PH))
        End Sub

        ''' <summary><paramref name="reserveRight"/>: header width kept free at the right (for a control placed there).</summary>
        Private Sub DrawCard(g As Graphics, r As Rectangle, title As String, hint As String, Optional reserveRight As Integer = 0)
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
            Dim headerRect As New Rectangle(r.X + pad, r.Y + Theme.Dpi(4), Math.Max(0, r.Width - pad * 2 - reserveRight), CardHeader - Theme.Dpi(8))
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
