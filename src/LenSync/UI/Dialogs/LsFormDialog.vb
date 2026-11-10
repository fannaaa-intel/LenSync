Option Strict On
Option Explicit On
Option Infer On

Imports System.Threading.Tasks
Imports LenSync.UI.Controls

Namespace UI.Dialogs

    ''' <summary>
    ''' A popup form on the LENSYNC card: spaced caption, heading, subtitle, a Body panel for fields, and
    ''' [Secondary] [Primary] buttons at the bottom right. Enter = primary (or the focused button), Esc = secondary.
    ''' Subclasses put controls in Body and implement OnSubmitAsync (return True to close with OK).
    ''' </summary>
    Public MustInherit Class LsFormDialog
        Inherits CardWindow

        Private Const BasePad As Integer = 36

        Protected ReadOnly Body As New Panel()
        Protected ReadOnly PrimaryButton As LsButton
        Protected ReadOnly SecondaryButton As LsButton

        Private _caption As String
        Private _heading As String
        Private _subtitle As String
        Private ReadOnly _bodyWidth As Integer          ' device px
        Private _bodyHeight As Integer                  ' device px (animated)
        Private _captionTop As Integer
        Private _headingRect As Rectangle
        Private _subtitleRect As Rectangle
        Private _busy As Boolean
        Private ReadOnly _heightTween As New Tween()

        Protected Sub New(caption As String, heading As String, subtitle As String,
                          primaryText As String, secondaryText As String, bodyWidth As Integer, bodyHeight As Integer)
            _caption = caption
            _heading = heading
            _subtitle = subtitle
            _bodyWidth = Theme.Dpi(bodyWidth)
            _bodyHeight = Theme.Dpi(bodyHeight)
            Text = heading

            Body.BackColor = Theme.White
            Controls.Add(Body)
            PrimaryButton = New LsButton(primaryText, ButtonVariant.Primary)
            SecondaryButton = New LsButton(secondaryText, ButtonVariant.Secondary)
            PrimaryButton.FitToText(140)
            SecondaryButton.FitToText(110)
            Controls.Add(SecondaryButton)
            Controls.Add(PrimaryButton)
            AcceptButton = PrimaryButton
            AddHandler PrimaryButton.Click, AddressOf OnPrimaryClick
            AddHandler SecondaryButton.Click, Sub() OnSecondary()
            LayoutCard()
        End Sub

        ''' <summary>Runs on Primary/Enter. Return True to close the dialog with DialogResult.OK.</summary>
        Protected MustOverride Function OnSubmitAsync() As Task(Of Boolean)

        ''' <summary>Secondary button and Esc. Default: close as Cancel.</summary>
        Protected Overridable Sub OnSecondary()
            If _busy Then Return
            CloseCard(DialogResult.Cancel)
        End Sub

        Protected Overrides Sub OnEscapePressed()
            OnSecondary()
        End Sub

        Private Async Sub OnPrimaryClick(sender As Object, e As EventArgs)
            If _busy OrElse IsClosing Then Return
            Dim ok As Boolean
            Try
                ok = Await OnSubmitAsync()
            Catch ex As Exception
                Core.AppLog.Write(ex, $"{Me.GetType().Name}.OnSubmit")
                SetBusy(False)
                LsDialog.Error(Core.Db.FriendlyMessage(ex))
                Return
            End Try
            If ok Then CloseCard(DialogResult.OK)
        End Sub

        ' ---------------------------------------------------------------- header / busy / height
        Protected Sub SetHeader(caption As String, heading As String, subtitle As String)
            _caption = caption
            _heading = heading
            _subtitle = subtitle
            Text = heading
            LayoutCard()
            Invalidate()
        End Sub

        Protected Sub SetPrimaryText(value As String)
            PrimaryButton.Text = value
            PrimaryButton.FitToText(120)
            LayoutCard()
        End Sub

        Protected Sub SetSecondaryText(value As String)
            SecondaryButton.Text = value
            SecondaryButton.FitToText(110)
            LayoutCard()
        End Sub

        ''' <summary>Disables the buttons and body while work runs; the primary button shows <paramref name="busyText"/>.</summary>
        Protected Sub SetBusy(busy As Boolean, Optional busyText As String = Nothing)
            If busy = _busy Then Return
            _busy = busy
            Body.Enabled = Not busy
            PrimaryButton.Enabled = Not busy
            SecondaryButton.Enabled = Not busy
            UseWaitCursor = busy
            If busy Then
                _idlePrimaryText = PrimaryButton.Text
                If busyText IsNot Nothing Then PrimaryButton.Text = busyText
            ElseIf _idlePrimaryText IsNot Nothing Then
                PrimaryButton.Text = _idlePrimaryText
                _idlePrimaryText = Nothing
            End If
        End Sub
        Private _idlePrimaryText As String

        Protected ReadOnly Property IsBusy As Boolean
            Get
                Return _busy
            End Get
        End Property

        ''' <summary>Animates the body (and the whole card) to a new height, keeping the card centered.</summary>
        Protected Sub SetBodyHeight(height96 As Integer)
            Dim target = Theme.Dpi(height96)
            If target = _bodyHeight Then Return
            Dim from = _bodyHeight
            If Not IsHandleCreated OrElse Not Visible Then
                _bodyHeight = target
                LayoutCard()
                Return
            End If
            _heightTween.Start(Theme.OpenMs,
                Sub(t)
                    _bodyHeight = from + CInt((target - from) * Easing.OutCubic(t))
                    LayoutCard()
                End Sub)
        End Sub

        Private Sub LayoutCard()
            Dim pad = Theme.Dpi(BasePad)
            Dim w = _bodyWidth + pad * 2
            Dim textW = w - pad * 2
            Dim y = Theme.Dpi(36)
            _captionTop = y
            y += Theme.Caption.Height + Theme.Dpi(6)
            Const wrap As TextFormatFlags = TextFormatFlags.WordBreak Or TextFormatFlags.NoPrefix Or TextFormatFlags.TextBoxControl Or TextFormatFlags.NoPadding
            Dim headH = TextRenderer.MeasureText(_heading, Theme.TitleSmall, New Size(textW, Integer.MaxValue), wrap).Height
            _headingRect = New Rectangle(pad, y, textW, headH)
            y += headH
            If Not String.IsNullOrEmpty(_subtitle) Then
                y += Theme.Dpi(4)
                Dim subH = TextRenderer.MeasureText(_subtitle, Theme.Body, New Size(textW, Integer.MaxValue), wrap).Height
                _subtitleRect = New Rectangle(pad, y, textW, subH)
                y += subH
            Else
                _subtitleRect = Rectangle.Empty
            End If
            y += Theme.Dpi(22)
            Body.Bounds = New Rectangle(pad, y, _bodyWidth, _bodyHeight)
            y += _bodyHeight + Theme.Dpi(20)

            Dim right = w - pad
            PrimaryButton.Location = New Point(right - PrimaryButton.Width, y)
            SecondaryButton.Location = New Point(PrimaryButton.Left - Theme.Dpi(10) - SecondaryButton.Width, y)
            y += PrimaryButton.Height + Theme.Dpi(32)

            Dim oldHeight = Height
            ClientSize = New Size(w, y)
            If Height <> oldHeight Then KeepCentered(oldHeight)
            Invalidate()
        End Sub

        Protected Overrides Sub PaintCard(g As Graphics)
            Dim pad = Theme.Dpi(BasePad)
            Gfx.DrawTracked(g, If(_caption, "").ToUpperInvariant(), Theme.Caption, Theme.Slate, pad, _captionTop, Theme.Dpi(2.2F))
            ' NoPadding keeps heading and subtitle flush with the caption and the field labels.
            Const flags As TextFormatFlags = TextFormatFlags.WordBreak Or TextFormatFlags.NoPrefix Or TextFormatFlags.TextBoxControl Or TextFormatFlags.NoPadding
            TextRenderer.DrawText(g, _heading, Theme.TitleSmall, _headingRect, Theme.Ink, flags)
            If Not _subtitleRect.IsEmpty Then
                TextRenderer.DrawText(g, _subtitle, Theme.Body, _subtitleRect, Theme.Slate, flags)
            End If
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then _heightTween.Dispose()
            MyBase.Dispose(disposing)
        End Sub

    End Class

End Namespace
