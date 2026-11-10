Option Strict On
Option Explicit On
Option Infer On

Imports System.Threading.Tasks
Imports LenSync.Core
Imports LenSync.Models
Imports LenSync.UI
Imports LenSync.UI.Controls
Imports LenSync.UI.Dialogs

''' <summary>
''' Asks for a reason, then runs an action that needs it (cancel a booking, void a sale). Heavier frame: these
''' actions can't be undone. The action runs off the UI thread; its field errors show under the reason box.
''' </summary>
Public Class ReasonDialog
    Inherits LsFormDialog

    Private ReadOnly _txtReason As New LsTextBox()
    Private ReadOnly _action As Func(Of String, AdminResult)

    Public Sub New(caption As String, heading As String, subtitle As String, primaryText As String, placeholder As String,
                   action As Func(Of String, AdminResult))
        MyBase.New(caption, heading, subtitle, primaryText, "Go back", 400, 88)
        _action = action
        StrongFrame = True
        Dim w = Theme.Dpi(400)
        Dim lbl As New Label() With {
            .Text = "Reason", .Font = Theme.LabelBold, .ForeColor = Theme.Charcoal, .TextAlign = ContentAlignment.BottomLeft,
            .Bounds = New Rectangle(0, 0, w, Theme.LabelBold.Height + Theme.Dpi(4))}
        _txtReason.FieldName = "Reason"
        _txtReason.MaxLength = 255
        _txtReason.Placeholder = placeholder
        _txtReason.Bounds = New Rectangle(0, lbl.Bottom + Theme.Dpi(4), w, Theme.Dpi(40))
        Body.Controls.Add(lbl)
        Body.Controls.Add(_txtReason)
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        _txtReason.Focus()
    End Sub

    Protected Overrides Async Function OnSubmitAsync() As Task(Of Boolean)
        Validator.ClearAll(Body)
        Dim p = BookingAdmin.ReasonProblem(_txtReason.Text)
        If p IsNot Nothing Then
            Validator.SetError(_txtReason, p)
            Shake()
            _txtReason.Focus()
            Return False
        End If
        Dim reason = _txtReason.Text.Trim()
        SetBusy(True, "Saving...")
        Dim result = Await Task.Run(Function() _action(reason))
        SetBusy(False)
        If FormErrors.Show(result, New Dictionary(Of String, Control) From {{"reason", _txtReason}}) Then
            Shake()
            Return False
        End If
        Return True
    End Function

End Class
