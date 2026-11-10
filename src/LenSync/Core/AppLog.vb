Option Strict On
Option Explicit On
Option Infer On

Imports System.IO
Imports System.Text

Namespace Core

    ''' <summary>
    ''' Technical error log in %AppData%\LenSync\logs (one file per day). Users see a friendly LsDialog;
    ''' the details land here. Never log passwords or reset codes.
    ''' </summary>
    Public NotInheritable Class AppLog

        Private Shared ReadOnly _gate As New Object()

        Private Sub New()
        End Sub

        Public Shared ReadOnly Property Folder As String
            Get
                Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LenSync", "logs")
            End Get
        End Property

        Public Shared Sub Write(ex As Exception, Optional context As String = Nothing)
            Dim sb As New StringBuilder()
            sb.Append("[").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("] ")
            sb.AppendLine(If(context, "Error"))
            If ex IsNot Nothing Then sb.AppendLine(ex.ToString())
            sb.AppendLine(New String("-"c, 80))
            Append(sb.ToString())
        End Sub

        Public Shared Sub Info(message As String)
            Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}")
        End Sub

        Private Shared Sub Append(text As String)
            ' Logging must never crash the app.
            Try
                SyncLock _gate
                    Directory.CreateDirectory(Folder)
                    File.AppendAllText(Path.Combine(Folder, $"lensync-{DateTime.Now:yyyyMMdd}.log"), text, Encoding.UTF8)
                End SyncLock
            Catch
            End Try
        End Sub

    End Class

End Namespace
