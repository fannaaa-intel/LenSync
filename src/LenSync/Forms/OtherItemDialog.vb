Option Strict On
Option Explicit On
Option Infer On

Imports System.Globalization
Imports System.Threading.Tasks
Imports LenSync.Core
Imports LenSync.Models
Imports LenSync.UI
Imports LenSync.UI.Controls
Imports LenSync.UI.Dialogs

''' <summary>An "Other" bill line: something not in the price list, with a description and price typed by staff.</summary>
Public Class OtherItemDialog
    Inherits LsFormDialog

    Private Const RowH As Integer = 86
    Private Const BodyW As Integer = 380

    Private ReadOnly _txtDesc As New LsTextBox()
    Private ReadOnly _txtPrice As New LsTextBox()
    Private ReadOnly _txtQty As New LsTextBox()

    Public Property Line As BillLine

    Public Sub New()
        MyBase.New("Other item", "Add an item not in the price list", "It's billed at the price you type here.",
                   "Add to bill", "Cancel", BodyW, RowH * 2 - 6)
        Dim w = Theme.Dpi(BodyW)
        Dim qtyW = Theme.Dpi(96)
        Place("Description", _txtDesc, 0, 0, w)
        Place("Price (₱)", _txtPrice, 0, RowH, w - qtyW - Theme.Dpi(16))
        Place("Quantity", _txtQty, w - qtyW, RowH, qtyW)
        _txtDesc.MaxLength = 255
        _txtDesc.Placeholder = "e.g. Photo restoration"
        _txtPrice.Filter = InputFilter.Money
        _txtPrice.MaxLength = 13
        _txtPrice.Placeholder = "e.g. 300.00"
        _txtQty.Filter = InputFilter.Digits
        _txtQty.MaxLength = 3
        _txtQty.Text = "1"
    End Sub

    Private Sub Place(caption As String, box As LsTextBox, x As Integer, y96 As Integer, w As Integer)
        Dim lbl As New Label() With {
            .Text = caption, .Font = Theme.LabelBold, .ForeColor = Theme.Charcoal, .TextAlign = ContentAlignment.BottomLeft,
            .Bounds = New Rectangle(x, Theme.Dpi(y96), w, Theme.LabelBold.Height + Theme.Dpi(4))}
        box.FieldName = caption
        box.Bounds = New Rectangle(x, lbl.Bottom + Theme.Dpi(4), w, Theme.Dpi(40))
        Body.Controls.Add(lbl)
        Body.Controls.Add(box)
    End Sub

    Protected Overrides Sub OnShown(e As EventArgs)
        MyBase.OnShown(e)
        _txtDesc.Focus()
    End Sub

    Protected Overrides Function OnSubmitAsync() As Task(Of Boolean)
        Validator.ClearAll(Body)
        Dim p = Billing.OtherDescriptionProblem(_txtDesc.Text)
        If p IsNot Nothing Then Validator.SetError(_txtDesc, p)
        Dim price As Decimal
        Dim problem As String = Nothing
        If Not Money.TryParse(_txtPrice.Text, price, problem) Then
            Validator.SetError(_txtPrice, problem)
        ElseIf price <= 0D Then
            Validator.SetError(_txtPrice, "The price must be more than ₱0.00.")
        End If
        Dim qty As Integer
        If Not Integer.TryParse(_txtQty.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, qty) Then qty = 0
        p = Billing.QtyProblem(qty)
        If p IsNot Nothing Then Validator.SetError(_txtQty, p)
        If Validator.HasErrors(Body) Then
            Shake()
            Validator.FocusFirstError(Body)
            Return Task.FromResult(False)
        End If
        Line = New BillLine With {.ItemType = ItemTypes.Other, .Description = _txtDesc.Text.Trim(), .Qty = qty, .UnitPrice = price}
        Return Task.FromResult(True)
    End Function

End Class
