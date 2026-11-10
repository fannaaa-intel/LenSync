Option Strict On
Option Explicit On
Option Infer On

Imports LenSync.Models

Namespace UI.Dialogs

    ''' <summary>Shows an AdminResult's errors: field errors inline under their control, the rest in a warning dialog.</summary>
    Public NotInheritable Class FormErrors

        Private Sub New()
        End Sub

        ''' <summary>Returns True if there were errors (and they are now on screen).</summary>
        Public Shared Function Show(result As AdminResult, fields As IDictionary(Of String, Control)) As Boolean
            If result Is Nothing OrElse result.Ok Then Return False
            Dim general As New List(Of String)
            Dim first As Control = Nothing
            For Each problem In result.Errors
                Dim target As Control = Nothing
                If fields IsNot Nothing AndAlso fields.TryGetValue(problem.Key, target) Then
                    Validator.SetError(target, problem.Value)
                    If first Is Nothing Then first = target
                Else
                    general.Add(problem.Value)
                End If
            Next
            If general.Count > 0 Then LsDialog.Warning(String.Join(Environment.NewLine, general), "Please check")
            first?.Focus()
            Return True
        End Function

    End Class

End Namespace
