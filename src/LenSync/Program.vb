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
        AddHandler AppDomain.CurrentDomain.UnhandledException, AddressOf OnDomainException

        ' Log in -> shell -> (Log out) -> log in again ... until the user closes a window.
        Do
            PrepareUiThread()
            Using login As New LoginForm()
                If login.ShowDialog() <> DialogResult.OK Then Exit Do
            End Using
            EnsureFirstRunSetup()

            Dim loggedOut As Boolean
            PrepareUiThread()
            Using main As New MainForm()
                Application.Run(main)
                loggedOut = main.SignedOut
            End Using
            ' End the session only after the shell (and every view in it) is fully disposed.
            Auth.SignOut(If(loggedOut, "Logged out", "Closed LENSYNC"))
            If Not loggedOut Then Exit Do
        Loop
    End Sub

    ''' <summary>
    ''' When an Application.Run loop ends, WinForms discards the thread's exception handler AND its
    ''' WindowsFormsSynchronizationContext. Without re-attaching both, after a log-out the next screen would show the
    ''' raw .NET crash box and "Await" would resume on a worker thread. So we set them up before every screen.
    ''' </summary>
    Private Sub PrepareUiThread()
        RemoveHandler Application.ThreadException, AddressOf OnUiThreadException
        AddHandler Application.ThreadException, AddressOf OnUiThreadException
        If TypeOf SynchronizationContext.Current IsNot WindowsFormsSynchronizationContext Then
            SynchronizationContext.SetSynchronizationContext(New WindowsFormsSynchronizationContext())
        End If
    End Sub

    ''' <summary>One-time setup that needs the database (e.g. the report verification secret).</summary>
    Private Sub EnsureFirstRunSetup()
        Try
            AppSettings.EnsureReportSecret()
        Catch ex As Exception
            AppLog.Write(ex, "First-run setup")
        End Try
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
