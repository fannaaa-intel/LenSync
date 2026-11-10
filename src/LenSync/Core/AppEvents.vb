Option Strict On
Option Explicit On
Option Infer On

Namespace Core

    ''' <summary>
    ''' App-wide notifications so screens refresh when shared data changes (e.g. Settings renames the studio and the
    ''' sidebar updates). Raise on the UI thread. Subscribers must unsubscribe when disposed.
    ''' </summary>
    Public NotInheritable Class AppEvents

        Private Sub New()
        End Sub

        ''' <summary>Business name, address, contact, logo or signature changed.</summary>
        Public Shared Event StudioChanged As EventHandler

        ''' <summary>The signed-in user's own name/email/role was edited.</summary>
        Public Shared Event CurrentUserChanged As EventHandler

        Public Shared Sub RaiseStudioChanged()
            RaiseEvent StudioChanged(Nothing, EventArgs.Empty)
        End Sub

        Public Shared Sub RaiseCurrentUserChanged()
            RaiseEvent CurrentUserChanged(Nothing, EventArgs.Empty)
        End Sub

    End Class

End Namespace
