Option Strict On
Option Explicit On
Option Infer On

Imports LenSync.Models
Imports LenSync.Modules.Settings
Imports LenSync.UI
Imports LenSync.UI.Controls
Imports LenSync.UI.Dialogs

Namespace Modules

    ''' <summary>Views that may hold unsaved edits; MainForm asks before switching away.</summary>
    Public Interface IGuardedView
        ''' <summary>Returns False to stay (the user chose to keep editing).</summary>
        Function CanLeave() As Boolean
    End Interface

    ''' <summary>
    ''' Settings module (Admin): tabs for Users, Roles &amp; access, Packages, Add-ons, Studio and Email.
    ''' Each tab is a SettingsPage on a white card; pages are created on first visit and slide in.
    ''' </summary>
    Public Class SettingsView
        Inherits UserControl
        Implements IGuardedView

        Private ReadOnly _tabs As New LsTabs()
        Private ReadOnly _card As New CardHost()
        Private ReadOnly _pages(5) As SettingsPage
        Private ReadOnly _slide As New Tween()
        Private _current As SettingsPage
        Private _switching As Boolean

        Private Shared ReadOnly TabNames As String() = {"Users", "Roles & access", "Packages", "Add-ons", "Studio", "Email"}

        Public Sub New()
            AutoScaleMode = AutoScaleMode.None
            BackColor = Theme.Snow
            DoubleBuffered = True
            _tabs.SetItems(TabNames)
            Controls.Add(_card)
            Controls.Add(_tabs)
            AddHandler _tabs.SelectedIndexChanged, AddressOf OnTabChanged
            AddHandler _card.Layout, Sub() LayoutPage()
        End Sub

        Protected Overrides Sub OnLoad(e As EventArgs)
            MyBase.OnLoad(e)
            _tabs.SelectedIndex = 0
        End Sub

        Private Function CreatePage(index As Integer) As SettingsPage
            Select Case index
                Case 0 : Return New UsersPage()
                Case 1 : Return New AccessPage()
                Case 2 : Return New PricingPage(PriceItemKind.Package)
                Case 3 : Return New PricingPage(PriceItemKind.Addon)
                Case 4 : Return New StudioPage()
                Case Else : Return New EmailPage()
            End Select
        End Function

        Private _lastIndex As Integer = -1

        Private Async Sub OnTabChanged(sender As Object, e As EventArgs)
            If _switching Then Return
            Dim index = _tabs.SelectedIndex
            If _current IsNot Nothing AndAlso Not ConfirmLeave(_current) Then
                ' Stay: put the tab underline back without re-entering this handler.
                _switching = True
                _tabs.SelectedIndex = _lastIndex
                _switching = False
                Return
            End If
            _lastIndex = index
            If _pages(index) Is Nothing Then
                _pages(index) = CreatePage(index)
                _pages(index).Visible = False
                _card.Controls.Add(_pages(index))
            End If
            Dim page = _pages(index)
            If _current IsNot Nothing AndAlso _current IsNot page Then _current.Visible = False
            _current = page
            Dim target = PageBounds()
            Dim offset = Theme.Dpi(24)
            page.Bounds = New Rectangle(target.X + offset, target.Y, target.Width, target.Height)
            page.Visible = True
            _slide.Start(Theme.OpenMs,
                Sub(t) page.Left = target.X + CInt(offset * (1 - Easing.OutCubic(t))),
                Sub() page.Bounds = PageBounds())
            Await page.EnsureLoadedAsync()
        End Sub

        Private Shared Function ConfirmLeave(page As SettingsPage) As Boolean
            If Not page.HasUnsavedChanges Then Return True
            If LsDialog.Confirm("Your changes on this tab haven't been saved.", "Discard changes?", "Discard", "Keep editing") Then
                page.DiscardChanges()
                Return True
            End If
            Return False
        End Function

        Public Function CanLeave() As Boolean Implements IGuardedView.CanLeave
            Return _current Is Nothing OrElse ConfirmLeave(_current)
        End Function

        Protected Overrides Sub OnLayout(e As LayoutEventArgs)
            MyBase.OnLayout(e)
            If _tabs Is Nothing Then Return
            Dim margin = Theme.Dpi(32)
            _tabs.Bounds = New Rectangle(margin, Theme.Dpi(14), Width - margin * 2, _tabs.Height)
            _card.Bounds = New Rectangle(margin, _tabs.Bottom + Theme.Dpi(18), Width - margin * 2, Math.Max(0, Height - _tabs.Bottom - Theme.Dpi(18) - Theme.Dpi(26)))
        End Sub

        Private Function PageBounds() As Rectangle
            Dim pad = Theme.Dpi(28)
            Return New Rectangle(pad, Theme.Dpi(24), Math.Max(0, _card.Width - pad * 2), Math.Max(0, _card.Height - Theme.Dpi(24) - Theme.Dpi(20)))
        End Function

        Private Sub LayoutPage()
            If _current IsNot Nothing AndAlso Not _slide.IsRunning Then _current.Bounds = PageBounds()
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then _slide.Dispose()
            MyBase.Dispose(disposing)
        End Sub

        ''' <summary>The white rounded card the pages sit on.</summary>
        Private NotInheritable Class CardHost
            Inherits Panel

            Public Sub New()
                DoubleBuffered = True
                SetStyle(ControlStyles.ResizeRedraw, True)
                BackColor = Theme.Snow
            End Sub

            Protected Overrides Sub OnPaint(e As PaintEventArgs)
                MyBase.OnPaint(e)
                Dim g = e.Graphics
                Gfx.HighQuality(g)
                Using path = Gfx.RoundedRect(New RectangleF(0.5F, 0.5F, Width - 1.5F, Height - 1.5F), Theme.Dpi(CSng(Theme.Radius)))
                    Using b As New SolidBrush(Theme.White)
                        g.FillPath(b, path)
                    End Using
                    Using p As New Pen(Theme.Mist)
                        g.DrawPath(p, path)
                    End Using
                End Using
            End Sub
        End Class

    End Class

End Namespace
