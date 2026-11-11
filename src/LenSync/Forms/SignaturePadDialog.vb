Option Strict On
Option Explicit On
Option Infer On

Imports System.Threading.Tasks
Imports LenSync.UI
Imports LenSync.UI.Controls
Imports LenSync.UI.Dialogs

''' <summary>
''' Settings > Studio > Draw: the owner signs on screen. The drawing becomes the same signature picture an upload
''' would (Result); StudioPage saves it with the rest of the studio details.
''' </summary>
Public Class SignaturePadDialog
    Inherits LsFormDialog

    Private Const BodyW As Integer = 480
    Private Const PadH As Integer = 200

    Private ReadOnly _pad As New SignaturePad()
    Private ReadOnly _btnUndo As New LsButton("Undo", ButtonVariant.Ghost)
    Private ReadOnly _btnClear As New LsButton("Clear", ButtonVariant.Ghost)

    ''' <summary>The signature (transparent PNG-ready bitmap). The caller owns and disposes it.</summary>
    Public Property Result As Bitmap

    Public Sub New()
        MyBase.New("E-signature", "Draw the owner's signature",
                   "Use the mouse, touchpad or a pen. It prints on reports above ""Owner's signature"".",
                   "Use signature", "Cancel", BodyW, PadH + 22 + 44)
        Dim w = Theme.Dpi(BodyW)
        _pad.Bounds = New Rectangle(0, 0, w, Theme.Dpi(PadH))
        _btnUndo.FitToText(80)
        _btnClear.FitToText(80)
        ' Room for the validator message under the pad, then the pad tools on the right.
        Dim toolsY = _pad.Bottom + Theme.Dpi(26)
        _btnClear.Location = New Point(w - _btnClear.Width, toolsY)
        _btnUndo.Location = New Point(_btnClear.Left - Theme.Dpi(6) - _btnUndo.Width, toolsY)
        Body.Controls.AddRange({_pad, _btnUndo, _btnClear})

        AddHandler _btnUndo.Click, Sub() _pad.Undo()
        AddHandler _btnClear.Click, Sub() _pad.Clear()
        AddHandler _pad.InkChanged, Sub()
                                        Validator.Clear(_pad)
                                        UpdateTools()
                                    End Sub
        UpdateTools()
    End Sub

    Private Sub UpdateTools()
        _btnUndo.Enabled = Not _pad.IsEmpty
        _btnClear.Enabled = Not _pad.IsEmpty
    End Sub

    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        If e.Control AndAlso e.KeyCode = Keys.Z Then
            _pad.Undo()
            e.Handled = True
            Return
        End If
        MyBase.OnKeyDown(e)
    End Sub

    Protected Overrides Function OnSubmitAsync() As Task(Of Boolean)
        If _pad.IsEmpty Then
            Validator.SetError(_pad, "Draw the signature first.")
            Shake()
            Return Task.FromResult(False)
        End If
        Result = _pad.ToImage()
        Return Task.FromResult(True)
    End Function

End Class
