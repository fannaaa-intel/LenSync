Option Strict On
Option Explicit On
Option Infer On

Imports System.Data
Imports MySql.Data.MySqlClient
Imports LenSync.Core
Imports LenSync.Models

Namespace Data

    ''' <summary>
    ''' SQL for packages and add-ons. Both tables share the same shape (add-ons have no inclusions), so one repository
    ''' serves both; the table name comes from a fixed whitelist, never from user input.
    ''' </summary>
    Public NotInheritable Class PriceItemRepository

        Private Sub New()
        End Sub

        Private Shared Function Table(kind As PriceItemKind) As String
            Return If(kind = PriceItemKind.Package, "packages", "addons")
        End Function

        Private Shared Function InclusionsColumn(kind As PriceItemKind) As String
            Return If(kind = PriceItemKind.Package, "IFNULL(inclusions, '')", "''")
        End Function

        Public Shared Function List(kind As PriceItemKind) As List(Of PriceItem)
            Dim sql = $"SELECT id, name, {InclusionsColumn(kind)} AS inclusions, price, is_active FROM {Table(kind)} ORDER BY is_active DESC, name"
            Return Db.Query(sql).Rows.Cast(Of DataRow)().Select(AddressOf Map).ToList()
        End Function

        Public Shared Function GetById(kind As PriceItemKind, id As Integer) As PriceItem
            Dim t = Db.Query($"SELECT id, name, {InclusionsColumn(kind)} AS inclusions, price, is_active FROM {Table(kind)} WHERE id = @id",
                             Db.P("@id", id))
            Return If(t.Rows.Count = 0, Nothing, Map(t.Rows(0)))
        End Function

        Private Shared Function Map(r As DataRow) As PriceItem
            Return New PriceItem With {
                .Id = Db.Int(r, "id"), .Name = Db.Str(r, "name"), .Inclusions = Db.Str(r, "inclusions"),
                .Price = Convert.ToDecimal(r("price")), .IsActive = Db.Bool(r, "is_active")}
        End Function

        ''' <summary>True if another active item already uses <paramref name="name"/> (case-insensitive).</summary>
        Public Shared Function ActiveNameExists(kind As PriceItemKind, name As String, excludeId As Integer) As Boolean
            Return Db.Scalar(Of Integer)($"SELECT COUNT(*) FROM {Table(kind)} WHERE name = @n AND is_active = 1 AND id <> @id",
                                         Db.P("@n", name), Db.P("@id", excludeId)) > 0
        End Function

        Public Shared Function Insert(conn As MySqlConnection, tx As MySqlTransaction, kind As PriceItemKind, item As PriceItem) As Integer
            Dim sql = If(kind = PriceItemKind.Package,
                         "INSERT INTO packages (name, inclusions, price) VALUES (@n, @i, @p); SELECT LAST_INSERT_ID();",
                         "INSERT INTO addons (name, price) VALUES (@n, @p); SELECT LAST_INSERT_ID();")
            Using cmd = Db.Command(conn, sql, tx, Db.P("@n", item.Name), Db.P("@i", item.Inclusions), Db.P("@p", item.Price))
                Return Db.ConvertValue(Of Integer)(cmd.ExecuteScalar())
            End Using
        End Function

        Public Shared Sub Update(conn As MySqlConnection, tx As MySqlTransaction, kind As PriceItemKind, item As PriceItem)
            Dim sql = If(kind = PriceItemKind.Package,
                         "UPDATE packages SET name = @n, inclusions = @i, price = @p WHERE id = @id",
                         "UPDATE addons SET name = @n, price = @p WHERE id = @id")
            Using cmd = Db.Command(conn, sql, tx, Db.P("@n", item.Name), Db.P("@i", item.Inclusions), Db.P("@p", item.Price), Db.P("@id", item.Id))
                cmd.ExecuteNonQuery()
            End Using
        End Sub

        Public Shared Sub SetActive(conn As MySqlConnection, tx As MySqlTransaction, kind As PriceItemKind, id As Integer, active As Boolean)
            Using cmd = Db.Command(conn, $"UPDATE {Table(kind)} SET is_active = @a WHERE id = @id", tx,
                                   Db.P("@a", If(active, 1, 0)), Db.P("@id", id))
                cmd.ExecuteNonQuery()
            End Using
        End Sub

    End Class

End Namespace
