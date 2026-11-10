Option Strict On
Option Explicit On
Option Infer On

Imports System.Drawing.Drawing2D
Imports System.Runtime.InteropServices

Namespace UI

    ''' <summary>Small Win32 helpers for borderless popups (rounded corners, drop shadow, no-activate).</summary>
    Friend NotInheritable Class Native

        Private Sub New()
        End Sub

        Public Const CS_DROPSHADOW As Integer = &H20000
        Public Const WS_EX_TOOLWINDOW As Integer = &H80
        Public Const WS_EX_NOACTIVATE As Integer = &H8000000
        Public Const WS_EX_TOPMOST As Integer = &H8

        Private Const DWMWA_WINDOW_CORNER_PREFERENCE As Integer = 33
        Private Const DWMWCP_ROUND As Integer = 2
        Private Const DWMWCP_ROUNDSMALL As Integer = 3

        <DllImport("dwmapi.dll", PreserveSig:=True)>
        Private Shared Function DwmSetWindowAttribute(hwnd As IntPtr, attribute As Integer, ByRef value As Integer, size As Integer) As Integer
        End Function

        Private Const DWMWA_EXTENDED_FRAME_BOUNDS As Integer = 9

        <StructLayout(LayoutKind.Sequential)>
        Private Structure RECT
            Public Left As Integer
            Public Top As Integer
            Public Right As Integer
            Public Bottom As Integer
        End Structure

        <DllImport("dwmapi.dll", PreserveSig:=True)>
        Private Shared Function DwmGetWindowAttribute(hwnd As IntPtr, attribute As Integer, ByRef value As RECT, size As Integer) As Integer
        End Function

        ''' <summary>The window's visible bounds (Form.Bounds includes DWM's invisible resize borders).</summary>
        Public Shared Function VisibleBounds(form As Form) As Rectangle
            Try
                Dim r As RECT
                If DwmGetWindowAttribute(form.Handle, DWMWA_EXTENDED_FRAME_BOUNDS, r, Marshal.SizeOf(GetType(RECT))) = 0 Then
                    Return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom)
                End If
            Catch ex As DllNotFoundException
            Catch ex As EntryPointNotFoundException
            End Try
            Return form.Bounds
        End Function

        ''' <summary>
        ''' Windows 11 draws smooth (anti-aliased) rounded corners for us. On Windows 10 the call fails and we fall back
        ''' to a Region, which is jagged but still rounded.
        ''' </summary>
        Public Shared Sub RoundCorners(form As Form, radius As Integer, Optional small As Boolean = False)
            Dim ok = False
            Try
                Dim pref = If(small, DWMWCP_ROUNDSMALL, DWMWCP_ROUND)
                ok = DwmSetWindowAttribute(form.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, pref, 4) = 0 AndAlso
                     Environment.OSVersion.Version.Build >= 22000
            Catch ex As DllNotFoundException
                ok = False
            Catch ex As EntryPointNotFoundException
                ok = False
            End Try
            If ok Then
                form.Region = Nothing
            Else
                Using path = Gfx.RoundedRect(New RectangleF(0, 0, form.Width, form.Height), radius)
                    form.Region = New Region(path)
                End Using
            End If
        End Sub

    End Class

End Namespace
