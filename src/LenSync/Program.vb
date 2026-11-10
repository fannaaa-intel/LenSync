Option Strict On
Option Explicit On
Option Infer On

Imports System.Threading
Imports LenSync.Core
Imports LenSync.UI.Dialogs

Friend Module Program

    <STAThread>
    Friend Sub Main()
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException)
        AddHandler Application.ThreadException, AddressOf OnUiThreadException
        AddHandler AppDomain.CurrentDomain.UnhandledException, AddressOf OnDomainException

        ' Phase 1: temporary test bench for the dialog/toast/validation system. Phase 2 replaces this with LoginForm.
        Application.Run(New DemoForm())
    End Sub

    ''' <summary>UI-boundary safety net: log the details, show a friendly message, keep the app running.</summary>
    Private Sub OnUiThreadException(sender As Object, e As ThreadExceptionEventArgs)
        AppLog.Write(e.Exception, "Unhandled UI exception")
        Try
            LsDialog.Error(Db.FriendlyMessage(e.Exception), "Unexpected error")
        Catch
            ' If even the dialog fails, the log entry above is all we can do.
        End Try
    End Sub

    Private Sub OnDomainException(sender As Object, e As UnhandledExceptionEventArgs)
        AppLog.Write(TryCast(e.ExceptionObject, Exception), "Fatal exception")
    End Sub

End Module
