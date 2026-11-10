Option Strict On
Option Explicit On
Option Infer On

Namespace UI

    ''' <summary>
    ''' LENSYNC design tokens. Minimalist monochrome: every color, font and size used by the UI lives here.
    ''' Meaning never depends on color; icons, weight, text and motion carry it.
    ''' </summary>
    Public NotInheritable Class Theme

        Private Sub New()
        End Sub

        ' ---------------------------------------------------------------- palette (black, white, grays only)
        Public Shared ReadOnly Ink As Color = ColorTranslator.FromHtml("#111111")       ' sidebar, primary buttons
        Public Shared ReadOnly Charcoal As Color = ColorTranslator.FromHtml("#2B2B2B")  ' body text, primary hover
        Public Shared ReadOnly Slate As Color = ColorTranslator.FromHtml("#6B6B6B")     ' secondary text
        Public Shared ReadOnly Silver As Color = ColorTranslator.FromHtml("#A3A3A3")    ' borders, disabled
        Public Shared ReadOnly Mist As Color = ColorTranslator.FromHtml("#E5E5E5")      ' dividers
        Public Shared ReadOnly Snow As Color = ColorTranslator.FromHtml("#F5F5F5")      ' page background
        Public Shared ReadOnly White As Color = Color.White                             ' cards
        ' Interaction shades (still grays) so hover/press never needs a new hue.
        Public Shared ReadOnly InkPressed As Color = Color.Black
        Public Shared ReadOnly SnowHover As Color = ColorTranslator.FromHtml("#EDEDED")
        Public Shared ReadOnly MistPressed As Color = ColorTranslator.FromHtml("#DCDCDC")
        Public Shared ReadOnly Overlay As Color = Color.Black

        ' ---------------------------------------------------------------- typography (Segoe UI only)
        Public Const FontFamilyName As String = "Segoe UI"
        Public Const FontFamilySemibold As String = "Segoe UI Semibold"

        Public Shared ReadOnly Body As New Font(FontFamilyName, 10.0F, FontStyle.Regular)
        Public Shared ReadOnly BodyBold As New Font(FontFamilySemibold, 10.0F, FontStyle.Regular)
        Public Shared ReadOnly Label As New Font(FontFamilyName, 9.0F, FontStyle.Regular)
        Public Shared ReadOnly LabelBold As New Font(FontFamilySemibold, 9.0F, FontStyle.Regular)
        Public Shared ReadOnly Caption As New Font(FontFamilySemibold, 8.0F, FontStyle.Regular)
        Public Shared ReadOnly Hint As New Font(FontFamilyName, 8.5F, FontStyle.Regular)
        Public Shared ReadOnly TitleSmall As New Font(FontFamilySemibold, 16.0F, FontStyle.Regular)
        Public Shared ReadOnly Title As New Font(FontFamilySemibold, 18.0F, FontStyle.Regular)
        Public Shared ReadOnly TitleLarge As New Font(FontFamilySemibold, 20.0F, FontStyle.Regular)
        Public Shared ReadOnly Button As New Font(FontFamilySemibold, 9.75F, FontStyle.Regular)

        ''' <summary>Printed documents (receipts, reports): point sizes for paper, same single typeface.</summary>
        Public NotInheritable Class Print
            Private Sub New()
            End Sub
            Public Shared ReadOnly Studio As New Font(FontFamilySemibold, 12.0F, FontStyle.Regular, GraphicsUnit.Point)
            Public Shared ReadOnly DocTitle As New Font(FontFamilySemibold, 9.5F, FontStyle.Regular, GraphicsUnit.Point)
            Public Shared ReadOnly Body As New Font(FontFamilyName, 8.0F, FontStyle.Regular, GraphicsUnit.Point)
            Public Shared ReadOnly BodyBold As New Font(FontFamilySemibold, 8.0F, FontStyle.Regular, GraphicsUnit.Point)
            Public Shared ReadOnly Small As New Font(FontFamilyName, 6.5F, FontStyle.Regular, GraphicsUnit.Point)
            Public Shared ReadOnly SmallBold As New Font(FontFamilySemibold, 6.5F, FontStyle.Regular, GraphicsUnit.Point)
            Public Shared ReadOnly Total As New Font(FontFamilySemibold, 11.0F, FontStyle.Regular, GraphicsUnit.Point)
            Public Shared ReadOnly Stamp As New Font(FontFamilySemibold, 54.0F, FontStyle.Regular, GraphicsUnit.Point)
        End Class

        ' ---------------------------------------------------------------- sizes (96-dpi values; wrap with Dpi())
        Public Const RadiusSmall As Integer = 6
        Public Const Radius As Integer = 8
        Public Const ButtonHeight As Integer = 38
        Public Const ButtonMinWidth As Integer = 112
        Public Const InputHeight As Integer = 32
        Public Const Pad As Integer = 24
        Public Const Gap As Integer = 12
        Public Const UnderlineThickness As Integer = 3

        ' ---------------------------------------------------------------- motion (all < 250 ms)
        Public Const FrameMs As Integer = 15
        Public Const OpenMs As Integer = 220
        Public Const CloseMs As Integer = 160
        Public Const ShakeMs As Integer = 240
        Public Const HoverMs As Integer = 120
        Public Const OverlayOpacity As Double = 0.42
        Public Const SuccessAutoCloseMs As Integer = 2500
        Public Const ToastVisibleMs As Integer = 3200

        ' ---------------------------------------------------------------- formats
        Public Const MoneyFormat As String = "₱#,##0.00"

        ' ---------------------------------------------------------------- DPI
        Private Shared _scale As Single = 0

        ''' <summary>Screen scale factor (1.0 at 100%, 1.5 at 150%). The app is system-DPI aware.</summary>
        Public Shared ReadOnly Property DpiScale As Single
            Get
                If _scale <= 0 Then
                    Using g = Graphics.FromHwnd(IntPtr.Zero)
                        _scale = g.DpiX / 96.0F
                    End Using
                End If
                Return _scale
            End Get
        End Property

        ''' <summary>Converts a 96-dpi pixel value to device pixels. Use for every size set in code.</summary>
        Public Shared Function Dpi(value As Integer) As Integer
            Return CInt(Math.Round(value * DpiScale))
        End Function

        Public Shared Function Dpi(value As Single) As Single
            Return value * DpiScale
        End Function

        Public Shared Function Dpi(width As Integer, height As Integer) As Size
            Return New Size(Dpi(width), Dpi(height))
        End Function

        ''' <summary>Linear blend between two theme colors (used by hover/press transitions).</summary>
        Public Shared Function Blend(fromColor As Color, toColor As Color, t As Double) As Color
            t = Math.Max(0, Math.Min(1, t))
            Return Color.FromArgb(
                CInt(fromColor.A + (CInt(toColor.A) - fromColor.A) * t),
                CInt(fromColor.R + (CInt(toColor.R) - fromColor.R) * t),
                CInt(fromColor.G + (CInt(toColor.G) - fromColor.G) * t),
                CInt(fromColor.B + (CInt(toColor.B) - fromColor.B) * t))
        End Function

    End Class

End Namespace
