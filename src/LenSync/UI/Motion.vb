Option Strict On
Option Explicit On
Option Infer On

Imports System.Diagnostics

Namespace UI

    ''' <summary>Easing curves. Input and output are 0..1.</summary>
    Public NotInheritable Class Easing

        Private Sub New()
        End Sub

        Public Shared Function OutCubic(t As Double) As Double
            t = Clamp(t)
            Return 1 - Math.Pow(1 - t, 3)
        End Function

        Public Shared Function InCubic(t As Double) As Double
            t = Clamp(t)
            Return t * t * t
        End Function

        Public Shared Function OutBack(t As Double) As Double
            ' Slight overshoot, used for the icon "pop".
            t = Clamp(t)
            Const c1 As Double = 1.70158
            Const c3 As Double = c1 + 1
            Return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2)
        End Function

        ''' <summary>Damped sine for horizontal shakes; returns -1..1 that settles to 0 at t = 1.</summary>
        Public Shared Function Shake(t As Double) As Double
            t = Clamp(t)
            Return Math.Sin(t * Math.PI * 6) * (1 - t)
        End Function

        Public Shared Function Clamp(t As Double) As Double
            Return Math.Max(0, Math.Min(1, t))
        End Function

    End Class

    ''' <summary>
    ''' One short animation driven by a WinForms Timer (~15 ms). The callback receives linear progress 0..1;
    ''' apply an Easing curve inside it. Calling Start again restarts the animation.
    ''' </summary>
    Public NotInheritable Class Tween
        Implements IDisposable

        Private ReadOnly _timer As New Timer() With {.Interval = Theme.FrameMs}
        Private ReadOnly _clock As New Stopwatch()
        Private _durationMs As Double
        Private _onFrame As Action(Of Double)
        Private _onDone As Action

        Public Sub New()
            AddHandler _timer.Tick, AddressOf OnTick
        End Sub

        Public ReadOnly Property IsRunning As Boolean
            Get
                Return _timer.Enabled
            End Get
        End Property

        Public Sub Start(durationMs As Integer, onFrame As Action(Of Double), Optional onDone As Action = Nothing)
            _durationMs = Math.Max(1, durationMs)
            _onFrame = onFrame
            _onDone = onDone
            _clock.Restart()
            _onFrame?.Invoke(0)
            _timer.Start()
        End Sub

        Public Sub [Stop]()
            _timer.Stop()
            _clock.Stop()
        End Sub

        Private Sub OnTick(sender As Object, e As EventArgs)
            Dim t = Math.Min(1.0, _clock.Elapsed.TotalMilliseconds / _durationMs)
            _onFrame?.Invoke(t)
            If t >= 1.0 Then
                _timer.Stop()
                Dim done = _onDone
                _onDone = Nothing
                done?.Invoke()
            End If
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            _timer.Stop()
            _timer.Dispose()
        End Sub

    End Class

End Namespace
