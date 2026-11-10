Option Strict On
Option Explicit On
Option Infer On

Imports System.Threading.Tasks
Imports LenSync.Core
Imports LenSync.UI
Imports LenSync.UI.Controls
Imports LenSync.UI.Dialogs

Namespace Modules.Settings

    ''' <summary>Settings > Studio: business name, address, contact, logo and owner signature (used on receipts and reports).</summary>
    Public Class StudioPage
        Inherits SettingsPage

        Private ReadOnly _lblName As Label
        Private ReadOnly _lblAddress As Label
        Private ReadOnly _lblContact As Label
        Private ReadOnly _lblLogo As Label
        Private ReadOnly _lblSignature As Label
        Private ReadOnly _txtName As New LsTextBox()
        Private ReadOnly _txtAddress As New LsTextBox()
        Private ReadOnly _txtContact As New LsTextBox()
        Private ReadOnly _logo As New ImageSlot() With {.EmptyText = "No logo yet"}
        Private ReadOnly _signature As New ImageSlot() With {.EmptyText = "No signature yet"}
        Private ReadOnly _btnLogo As New LsButton("Choose...", ButtonVariant.Secondary)
        Private ReadOnly _btnLogoRemove As New LsButton("Remove", ButtonVariant.Ghost)
        Private ReadOnly _btnSig As New LsButton("Choose...", ButtonVariant.Secondary)
        Private ReadOnly _btnSigRemove As New LsButton("Remove", ButtonVariant.Ghost)
        Private ReadOnly _hint As New Label()
        Private ReadOnly _btnSave As New LsButton("Save changes", ButtonVariant.Primary)
        Private ReadOnly _btnDiscard As New LsButton("Discard", ButtonVariant.Secondary)

        Private _saved As New StudioInfo()
        Private _logoSource As String          ' Nothing = unchanged, "" = remove, path = new picture
        Private _signatureSource As String
        Private _filling As Boolean

        Public Sub New()
            MyBase.New("Studio", "Your business details. They appear on the log-in screen, receipts and printed reports.")
            AddAction(_btnSave)
            AddAction(_btnDiscard)
            _lblName = AddFieldLabel("Business name")
            _lblAddress = AddFieldLabel("Address")
            _lblContact = AddFieldLabel("Contact number")
            _lblLogo = AddFieldLabel("Logo")
            _lblSignature = AddFieldLabel("Owner's signature")
            _txtName.FieldName = "Business name"
            _txtName.MaxLength = 120
            _txtAddress.FieldName = "Address"
            _txtAddress.MaxLength = 255
            _txtAddress.Placeholder = "e.g. Centro, Aparri, Cagayan"
            _txtContact.FieldName = "Contact number"
            _txtContact.Filter = InputFilter.Phone
            _txtContact.MaxLength = 40
            _txtContact.Placeholder = "e.g. 0917 123 4567"
            _hint.Text = "PNG or JPG up to 5 MB. A signature on a white or transparent background prints best."
            _hint.Font = Theme.Hint
            _hint.ForeColor = Theme.Slate
            _hint.BackColor = Theme.White
            For Each b In {_btnLogo, _btnLogoRemove, _btnSig, _btnSigRemove}
                b.FitToText(96)
            Next
            Controls.AddRange({_txtName, _txtAddress, _txtContact, _logo, _signature, _btnLogo, _btnLogoRemove, _btnSig, _btnSigRemove, _hint})

            For Each t In {_txtName, _txtAddress, _txtContact}
                AddHandler t.TextChanged, Sub() UpdateButtons()
            Next
            AddHandler _btnLogo.Click, Sub() ChooseImage(isLogo:=True)
            AddHandler _btnSig.Click, Sub() ChooseImage(isLogo:=False)
            AddHandler _btnLogoRemove.Click, Sub() RemoveImage(isLogo:=True)
            AddHandler _btnSigRemove.Click, Sub() RemoveImage(isLogo:=False)
            AddHandler _btnSave.Click, Sub() SaveAsync()
            AddHandler _btnDiscard.Click, Sub() DiscardChanges()
            UpdateButtons()
        End Sub

        Public Overrides Async Function ReloadAsync() As Task
            Dim info = Await RunAsync(Function() StudioAdmin.LoadStudio(), "Loading studio details")
            If info Is Nothing Then Return
            _saved = info
            Fill()
        End Function

        Private Sub Fill()
            _filling = True
            _txtName.Text = _saved.BusinessName
            _txtAddress.Text = _saved.Address
            _txtContact.Text = _saved.Contact
            _logo.Picture = TryLoad(_saved.LogoPath)
            _signature.Picture = TryLoad(_saved.SignaturePath)
            _logoSource = Nothing
            _signatureSource = Nothing
            _filling = False
            Validator.ClearAll(Me)
            UpdateButtons()
        End Sub

        Private Shared Function TryLoad(path As String) As Image
            If String.IsNullOrEmpty(path) OrElse Not IO.File.Exists(path) Then Return Nothing
            Try
                Return StudioAdmin.LoadImageCopy(path)
            Catch ex As Exception
                AppLog.Write(ex, "Loading " & path)
                Return Nothing
            End Try
        End Function

        Public Overrides ReadOnly Property HasUnsavedChanges As Boolean
            Get
                Return _txtName.Text <> _saved.BusinessName OrElse _txtAddress.Text <> _saved.Address OrElse
                       _txtContact.Text <> _saved.Contact OrElse _logoSource IsNot Nothing OrElse _signatureSource IsNot Nothing
            End Get
        End Property

        Public Overrides Sub DiscardChanges()
            Fill()
        End Sub

        Private Sub UpdateButtons()
            If _filling Then Return
            Dim dirty = HasUnsavedChanges
            _btnSave.Enabled = dirty
            _btnDiscard.Enabled = dirty
            _btnLogoRemove.Enabled = _logo.Picture IsNot Nothing
            _btnSigRemove.Enabled = _signature.Picture IsNot Nothing
        End Sub

        Private Sub ChooseImage(isLogo As Boolean)
            Using dlg As New OpenFileDialog() With {
                .Title = If(isLogo, "Choose the studio logo", "Choose the owner's signature"),
                .Filter = "Pictures (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
                .CheckFileExists = True, .Multiselect = False}
                If dlg.ShowDialog(FindForm()) <> DialogResult.OK Then Return
                Dim preview As Image
                Try
                    preview = StudioAdmin.LoadImageCopy(dlg.FileName)
                Catch ex As Exception
                    AppLog.Write(ex, "Opening picture")
                    Validator.SetError(If(isLogo, _logo, _signature), "That file isn't a picture LENSYNC can read.")
                    Return
                End Try
                Validator.Clear(If(isLogo, _logo, _signature))
                If isLogo Then
                    _logoSource = dlg.FileName
                    _logo.Picture = preview
                Else
                    _signatureSource = dlg.FileName
                    _signature.Picture = preview
                End If
            End Using
            UpdateButtons()
        End Sub

        Private Sub RemoveImage(isLogo As Boolean)
            If isLogo Then
                _logoSource = If(String.IsNullOrEmpty(_saved.LogoPath), Nothing, "")
                _logo.Picture = Nothing
            Else
                _signatureSource = If(String.IsNullOrEmpty(_saved.SignaturePath), Nothing, "")
                _signature.Picture = Nothing
            End If
            UpdateButtons()
        End Sub

        Private Async Sub SaveAsync()
            Validator.ClearAll(Me)
            Dim info As New StudioInfo With {.BusinessName = _txtName.Text, .Address = _txtAddress.Text, .Contact = _txtContact.Text}
            Dim logo = _logoSource
            Dim sig = _signatureSource
            _btnSave.Enabled = False
            Dim result = Await RunAsync(Function() StudioAdmin.SaveStudio(info, logo, sig), "Saving studio details")
            If result Is Nothing Then
                UpdateButtons()
                Return
            End If
            Dim fields As New Dictionary(Of String, Control) From {
                {"name", _txtName}, {"address", _txtAddress}, {"contact", _txtContact}, {"logo", _logo}, {"signature", _signature}}
            If FormErrors.Show(result, fields) Then
                UpdateButtons()
                Return
            End If
            Await ReloadAsync()
            AppEvents.RaiseStudioChanged()
            Toast.Show("Studio details saved.", ToastKind.Success)
        End Sub

        Protected Overrides Sub LayoutContent(area As Rectangle)
            Dim labelH = Theme.LabelBold.Height + Theme.Dpi(4)
            Dim fieldH = Theme.Dpi(42)
            Dim block = labelH + Theme.Dpi(4) + fieldH + Theme.Dpi(30)
            Dim colGap = Theme.Dpi(40)
            Dim leftW = Math.Min(Theme.Dpi(420), CInt(area.Width * 0.42))
            Dim y = area.Top
            For Each pair In {Tuple.Create(_lblName, _txtName), Tuple.Create(_lblAddress, _txtAddress), Tuple.Create(_lblContact, _txtContact)}
                pair.Item1.Bounds = New Rectangle(area.Left, y, leftW, labelH)
                pair.Item2.Bounds = New Rectangle(area.Left, y + labelH + Theme.Dpi(4), leftW, fieldH)
                y += block
            Next

            ' Pictures: two slots side by side in the right column.
            Dim x = area.Left + leftW + colGap
            Dim rightW = Math.Min(Theme.Dpi(480), area.Right - x)
            Dim slotW = (rightW - Theme.Dpi(20)) \ 2
            Dim slotH = Theme.Dpi(132)
            Dim top = area.Top
            Dim col = 0
            For Each set1 In {Tuple.Create(_lblLogo, _logo, _btnLogo, _btnLogoRemove), Tuple.Create(_lblSignature, _signature, _btnSig, _btnSigRemove)}
                Dim sx = x + col * (slotW + Theme.Dpi(20))
                set1.Item1.Bounds = New Rectangle(sx, top, slotW, labelH)
                set1.Item2.Bounds = New Rectangle(sx, top + labelH + Theme.Dpi(4), slotW, slotH)
                ' Choose / Remove share the slot's width so they never run into the next picture.
                Dim chooseW = CInt(slotW * 0.56)
                set1.Item3.Bounds = New Rectangle(sx, set1.Item2.Bottom + Theme.Dpi(26), chooseW, set1.Item3.Height)
                set1.Item4.Bounds = New Rectangle(set1.Item3.Right + Theme.Dpi(4), set1.Item3.Top, slotW - chooseW - Theme.Dpi(4), set1.Item4.Height)
                col += 1
            Next
            _hint.Bounds = New Rectangle(x, _btnLogo.Bottom + Theme.Dpi(12), rightW, Theme.Hint.Height * 2 + Theme.Dpi(4))
        End Sub

    End Class

End Namespace
