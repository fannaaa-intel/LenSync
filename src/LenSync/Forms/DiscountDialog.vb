Option Strict On
Option Explicit On
Option Infer On

Imports System.Threading.Tasks
Imports LenSync.Core
Imports LenSync.Models
Imports LenSync.UI
Imports LenSync.UI.Controls
Imports LenSync.UI.Dialogs

''' <summary>Add, edit or remove the bill's discount: a peso amount or a percent of the subtotal, with a required reason.</summary>
Public Class DiscountDialog
    Inherits LsFormDialog

    Private Const RowH As Integer = 86
    Private Const BodyW As Integer = 380

    Private ReadOnly _subtotal As Decimal
    Private ReadOnly _kind As New LsChoice()
    Private ReadOnly _txtValue As New LsTextBox()
    Private ReadOnly _txtReason As New LsTextBox()
    Private ReadOnly _lblValue As Label
    Private ReadOnly _lblPreview As New Label()
    Private ReadOnly _btnRemove As New LsButton("Remove discount", ButtonVariant.Ghost)

    ''' <summary>The discount to apply, or Nothing when the user removed it.</summary>
    Public Property Result As Discount

    Public Sub New(subtotal As Decimal, existing As Discount)
        MyBase.New("Discount", If(existing Is Nothing, "Add a discount", "Change the discount"),
                   $"Subtotal {Money.Format(subtotal)}. The reason is saved in the audit log.",
                   "Apply discount", "Cancel", BodyW, RowH * 3 - 6 + If(existing Is Nothing, 0, 46))
        _subtotal = subtotal
        Dim w = Theme.Dpi(BodyW)

        Body.Controls.Add(MakeLabel("Discount type", 0, w))
        _kind.SetItems({"Amount (₱)", "Percent (%)"})
        _kind.AccessibleName = "Discount type"
        _kind.Bounds = New Rectangle(0, Theme.Dpi(24), w, Theme.Dpi(40))
        Body.Controls.Add(_kind)

        _lblValue = MakeLabel("Amount (₱)", RowH, w)
        Body.Controls.Add(_lblValue)
        _lblPreview.Font = Theme.Label
        _lblPreview.ForeColor = Theme.Slate
        _lblPreview.TextAlign = ContentAlignment.BottomRight
        _lblPreview.Bounds = New Rectangle(w \ 3, _lblValue.Top, w - w \ 3, _lblValue.Height)
        Body.Controls.Add(_lblPreview)
        _lblPreview.BringToFront()
        _txtValue.Filter = InputFilter.Money
        _txtValue.MaxLength = 13
        _txtValue.Bounds = New Rectangle(0, _lblValue.Bottom + Theme.Dpi(4), w, Theme.Dpi(40))
        Body.Controls.Add(_txtValue)

        Dim lblReason = MakeLabel("Reason", RowH * 2, w)
        Body.Controls.Add(lblReason)
        _txtReason.FieldName = "Reason"
        _txtReason.MaxLength = 255
        _txtReason.Placeholder = "e.g. Senior citizen, loyal customer, promo"
        _txtReason.Bounds = New Rectangle(0, lblReason.Bottom + Theme.Dpi(4), w, Theme.Dpi(40))
        Body.Controls.Add(_txtReason)

        If existing IsNot Nothing Then
            _btnRemove.FitToText(120)
            _btnRemove.Location = New Point(0, Theme.Dpi(RowH * 3 - 6 + 8))
            Body.Controls.Add(_btnRemove)
            AddHandler _btnRemove.Click, Sub()
                                             Result = Nothing
                                             CloseCard(DialogResult.OK)
                                         End Sub
        End If

        AddHandler _kind.SelectedIndexChanged, Sub() UpdateKind()
        AddHandler _txtValue.TextChanged, Sub() UpdatePreview()
        _kind.SelectedIndex = If(existing IsNot Nothing AndAlso existing.Kind = DiscountTypes.Percent, 1, 0)
        If existing IsNot Nothing Then
            _txtValue.Text = If(existing.Kind = DiscountTypes.Percent, existing.Value.ToString("0.##", Globalization.CultureInfo.InvariantCulture),
                                Money.FormatPlain(existing.Value))
            _txtReason.Text = existing.Reason
        End If
        UpdateKind()
    End Sub

    Private Function MakeLabel(caption As String, y96 As Integer, w As Integer) As Label
        Return New Label() With {
            .Text = caption, .Font = Theme.LabelBold, .ForeColor = Theme.Charcoal, .TextAlign = ContentAlignment.BottomLeft,
            .Bounds = New Rectangle(0, Theme.Dpi(y96), w, Theme.LabelBold.Height + Theme.Dpi(4))}
    End Function

    Private ReadOnly Property Kind As String
        Get
            Return If(_kind.SelectedIndex = 1, DiscountTypes.Percent, DiscountTypes.Fixed)
        End Get
    End Property

    Private Sub UpdateKind()
        Dim percent = Kind = DiscountTypes.Percent
        _lblValue.Text = If(percent, "Percent (%)", "Amount (₱)")
        _txtValue.FieldName = _lblValue.Text
        _txtValue.Placeholder = If(percent, "e.g. 10", "e.g. 200.00")
        UpdatePreview()
    End Sub

    Private Sub UpdatePreview()
        Dim value As Decimal
        Dim problem As String = Nothing
        If Not Money.TryParse(_txtValue.Text, value, problem) OrElse value <= 0D Then
            _lblPreview.Text = ""
            Return
        End If
        Dim amount = Math.Min(Billing.DiscountAmountFor(_subtotal, New Discount With {.Kind = Kind, .Value = value}), _subtotal)
        _lblPreview.Text = $"−{Money.Format(amount)} off"
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        _txtValue.Focus()
    End Sub

    Protected Overrides Function OnSubmitAsync() As Task(Of Boolean)
        Validator.ClearAll(Body)
        Dim value As Decimal
        Dim problem As String = Nothing
        If Not Money.TryParse(_txtValue.Text, value, problem) Then
            Validator.SetError(_txtValue, If(_txtValue.Text.Trim().Length = 0, "Enter the discount.", problem))
        Else
            Dim p = Billing.DiscountValueProblem(Kind, value, _subtotal)
            If p IsNot Nothing Then Validator.SetError(_txtValue, p)
        End If
        Dim reasonProblem = BookingAdmin.ReasonProblem(_txtReason.Text)
        If reasonProblem IsNot Nothing Then Validator.SetError(_txtReason, reasonProblem)
        If Validator.HasErrors(Body) Then
            Shake()
            Validator.FocusFirstError(Body)
            Return Task.FromResult(False)
        End If
        Result = New Discount With {.Kind = Kind, .Value = value, .Reason = _txtReason.Text.Trim()}
        Return Task.FromResult(True)
    End Function

End Class
