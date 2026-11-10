Option Strict On
Option Explicit On
Option Infer On

Namespace UI.Dialogs

    ''' <summary>Borderless black sheet that fades in over the owner window behind a modal LsDialog.</summary>
    Friend NotInheritable Class DimOverlay
        Inherits Form

        Private ReadOnly _tween As New Tween()

        Public Sub New(owner As Form)
            FormBorderStyle = FormBorderStyle.None
            ShowInTaskbar = False
            StartPosition = FormStartPosition.Manual
            BackColor = Theme.Overlay
            Opacity = 0
            AutoScaleMode = AutoScaleMode.None
            Bounds = CoverBounds(owner)
            Me.Owner = owner
        End Sub

        Private Shared Function CoverBounds(owner As Form) As Rectangle
            ' Visible frame, so the title bar dims too and nothing spills past the window edge.
            Return Rectangle.Intersect(Native.VisibleBounds(owner), Screen.FromControl(owner).Bounds)
        End Function

        Protected Overrides ReadOnly Property ShowWithoutActivation As Boolean
            Get
                Return True
            End Get
        End Property

        Protected Overrides ReadOnly Property CreateParams As CreateParams
            Get
                Dim cp = MyBase.CreateParams
                cp.ExStyle = cp.ExStyle Or Native.WS_EX_TOOLWINDOW Or Native.WS_EX_NOACTIVATE
                Return cp
            End Get
        End Property

        Public Sub FadeIn()
            Show(Owner)
            _tween.Start(Theme.OpenMs, Sub(t) Opacity = Theme.OverlayOpacity * Easing.OutCubic(t))
        End Sub

        ''' <summary>Fades out, then closes and disposes itself.</summary>
        Public Sub FadeOut()
            Dim start = Opacity
            _tween.Start(Theme.CloseMs,
                Sub(t) Opacity = start * (1 - Easing.OutCubic(t)),
                Sub()
                    If IsDisposed Then Return
                    Close()
                    Dispose()
                End Sub)
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then _tween.Dispose()
            MyBase.Dispose(disposing)
        End Sub

    End Class

End Namespace
