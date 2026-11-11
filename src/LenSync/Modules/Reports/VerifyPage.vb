Option Strict On
Option Explicit On
Option Infer On

Imports System.Threading.Tasks
Imports LenSync.Core
Imports LenSync.Models
Imports LenSync.UI
Imports LenSync.UI.Controls
Imports LenSync.UI.Dialogs

Namespace Modules.Reports

    ''' <summary>
    ''' Reports > Verify: type the report no. and verification code from a paper report. The result card says whether
    ''' it is genuine (and whether the period's sales changed since it was printed), the code doesn't match, or no such
    ''' report was printed. Every check is audited (REPORT_VERIFY).
    ''' </summary>
    Public Class VerifyPage
        Inherits ModulePage

        Private ReadOnly _no As New LsTextBox()
        Private ReadOnly _code As New LsTextBox()
        Private ReadOnly _btnVerify As New LsButton("Verify report", ButtonVariant.Primary)
        Private ReadOnly _hint As New Label()
        Private ReadOnly _card As New VerifyResultCard()
        Private ReadOnly _lblNo As Label
        Private ReadOnly _lblCode As Label
        Private _busy As Boolean

        Public Sub New()
            MyBase.New("Verify", "Check that a paper report came from LENSYNC and that its figures still match the records.")
            _lblNo = AddFieldLabel("Report no.")
            _lblCode = AddFieldLabel("Verification code")
            _no.FieldName = "Report no."
            _no.Placeholder = "RPT-2026-0001"
            _no.MaxLength = 20
            _no.InnerTextBox.CharacterCasing = CharacterCasing.Upper
            _code.FieldName = "Verification code"
            _code.Placeholder = "ABCDE-FGH23"
            _code.MaxLength = 13
            _code.Icon = LineIcon.Key
            _code.InnerTextBox.CharacterCasing = CharacterCasing.Upper
            _hint.Font = Theme.Hint
            _hint.ForeColor = Theme.Slate
            _hint.BackColor = Theme.White
            _hint.Text = "Both are printed in the footer of every page of the report. Dashes and spaces in the code don't matter."
            Controls.AddRange({_no, _code, _btnVerify, _hint, _card})
            _no.TabIndex = 0
            _code.TabIndex = 1
            _btnVerify.TabIndex = 2

            For Each box In {_no, _code}
                AddHandler box.KeyDown, Sub(s, e)
                                            If e.KeyCode = Keys.Enter Then
                                                e.Handled = True
                                                e.SuppressKeyPress = True
                                                VerifyNow()
                                            End If
                                        End Sub
            Next
            AddHandler _btnVerify.Click, Sub() VerifyNow()
        End Sub

        Public Overrides Function ReloadAsync() As Task
            Return Task.CompletedTask
        End Function

        Protected Overrides Sub OnVisibleChanged(e As EventArgs)
            MyBase.OnVisibleChanged(e)
            If Visible AndAlso IsHandleCreated Then BeginInvoke(New Action(Sub() If Not IsDisposed Then _no.Focus()))
        End Sub

        Private Async Sub VerifyNow()
            If _busy Then Return
            Validator.ClearAll(Me)
            Dim noProblem = ReportService.ReportNoProblem(_no.Text)
            Dim codeProblem = ReportService.CodeProblem(_code.Text)
            If noProblem IsNot Nothing Then Validator.SetError(_no, noProblem)
            If codeProblem IsNot Nothing Then Validator.SetError(_code, codeProblem)
            If noProblem IsNot Nothing OrElse codeProblem IsNot Nothing Then
                Validator.FocusFirstError(Me)
                Return
            End If
            Dim no = ReportService.NormalizeReportNo(_no.Text)
            Dim code = _code.Text
            _busy = True
            _btnVerify.Enabled = False
            _btnVerify.Text = "Checking..."
            Try
                Dim result = Await RunAsync(Function() ReportService.Verify(no, code), "Verifying report " & no)
                If result Is Nothing OrElse IsDisposed Then Return
                _card.ShowResult(result, no)
            Finally
                _busy = False
                If Not IsDisposed Then
                    _btnVerify.Text = "Verify report"
                    _btnVerify.Enabled = True
                End If
            End Try
        End Sub

        Protected Overrides Sub LayoutContent(area As Rectangle)
            Dim colW = Math.Min(Theme.Dpi(300), Math.Max(Theme.Dpi(240), area.Width * 34 \ 100))
            Dim x = area.Left
            Dim y = area.Top
            Dim fieldH = Theme.Dpi(40)
            Dim lblH = Theme.LabelBold.Height + Theme.Dpi(6)
            Dim errRoom = Theme.Dpi(26)

            _lblNo.Bounds = New Rectangle(x, y, colW, lblH)
            y += lblH
            _no.Bounds = New Rectangle(x, y, colW, fieldH)
            y += fieldH + errRoom
            _lblCode.Bounds = New Rectangle(x, y, colW, lblH)
            y += lblH
            _code.Bounds = New Rectangle(x, y, colW, fieldH)
            y += fieldH + errRoom
            _btnVerify.Bounds = New Rectangle(x, y, colW, Theme.Dpi(Theme.ButtonHeight))
            y = _btnVerify.Bottom + Theme.Dpi(12)
            Dim hintH = TextRenderer.MeasureText(_hint.Text, Theme.Hint, New Size(colW, Integer.MaxValue), TextFormatFlags.WordBreak).Height + Theme.Dpi(4)
            _hint.Bounds = New Rectangle(x, y, colW, Math.Max(0, Math.Min(hintH, area.Bottom - y)))

            Dim cx = x + colW + Theme.Dpi(28)
            _card.Bounds = New Rectangle(cx, area.Top, Math.Max(0, area.Right - cx), area.Height)
        End Sub

    End Class

End Namespace
