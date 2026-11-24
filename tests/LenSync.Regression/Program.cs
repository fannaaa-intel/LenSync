using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LenSync;
using LenSync.Core;
using LenSync.Models;
using LenSync.Modules;
using LenSync.Modules.AddService;
using LenSync.Modules.Reports;
using LenSync.Modules.Settings;
using LenSync.UI.Controls;

static class H
{
    public static readonly List<string[]> Results = new List<string[]>();
    public static string Out;
    const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static bool Check(string id, string area, string name, bool ok, string detail = "")
    {
        Results.Add(new[] { id, area, ok ? "PASS" : "FAIL", name, detail ?? "" });
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {id} [{area}] {name}{(string.IsNullOrEmpty(detail) ? "" : " :: " + detail)}");
        return ok;
    }

    public static void Try(string id, string area, string name, Func<string> test)
    {
        try { var detail = test(); Check(id, area, name, detail == null || !detail.StartsWith("FAIL"), detail); }
        catch (Exception ex) { Check(id, area, name, false, "EXCEPTION " + ex.GetType().Name + ": " + ex.Message); }
    }

    public static object Field(object o, string name)
    {
        for (var t = o.GetType(); t != null; t = t.BaseType)
        {
            var f = t.GetField(name, All);
            if (f != null) return f.GetValue(o);
        }
        throw new MissingFieldException(o.GetType().Name, name);
    }

    public static T Field<T>(object o, string name) => (T)Field(o, name);

    public static void Click(Control c) =>
        typeof(Control).GetMethod("OnClick", All).Invoke(c, new object[] { EventArgs.Empty });

    /// <summary>Click after the current handler returns (a click that opens a modal dialog must not block the bot).</summary>
    public static void ClickLater(Control c) => c.BeginInvoke(new Action(() => Click(c)));

    public static IEnumerable<Control> Tree(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (var d in Tree(c)) yield return d;
        }
    }

    public static T Find<T>(Control root) where T : Control => Tree(root).OfType<T>().FirstOrDefault(c => c.Visible || true);

    public static void Snap(Control c, string name)
    {
        try
        {
            using (var bmp = new Bitmap(Math.Max(1, c.Width), Math.Max(1, c.Height)))
            {
                c.DrawToBitmap(bmp, new Rectangle(0, 0, c.Width, c.Height));
                bmp.Save(Path.Combine(Out, name + ".png"));
            }
        }
        catch (Exception ex) { Console.WriteLine("snap failed " + name + ": " + ex.Message); }
    }

    public static decimal Dec(string sql, params object[] kv) => Convert.ToDecimal(Db.Query(sql, P(kv)).Rows[0][0]);
    public static string Str(string sql, params object[] kv) { var v = Db.Query(sql, P(kv)).Rows[0][0]; return v is DBNull ? null : Convert.ToString(v); }
    static MySql.Data.MySqlClient.MySqlParameter[] P(object[] kv)
    {
        var list = new List<MySql.Data.MySqlClient.MySqlParameter>();
        for (int i = 0; i < kv.Length; i += 2) list.Add(Db.P((string)kv[i], kv[i + 1]));
        return list.ToArray();
    }
}

/// <summary>Answers popups the way a user would. Each handler gets every open card window; returning true consumes it.</summary>
class Bot
{
    readonly List<(string what, Func<Form, bool> handle)> _queue = new List<(string, Func<Form, bool>)>();
    readonly Dictionary<Form, DateTime> _seen = new Dictionary<Form, DateTime>();
    readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = 120 };
    public readonly List<string> Unexpected = new List<string>();

    public Bot() { _timer.Tick += (s, e) => Tick(); _timer.Start(); }

    public void Expect(string what, Func<Form, bool> handle) => _queue.Add((what, handle));
    public int Pending => _queue.Count;
    public void Clear() => _queue.Clear();

    bool _inTick;

    void Tick()
    {
        if (_inTick) return;   // a handler's click may run a modal loop that ticks us again
        _inTick = true;
        try { TickCore(); } finally { _inTick = false; }
    }

    void TickCore()
    {
        foreach (var f in Application.OpenForms.Cast<Form>().ToList())
        {
            var tn = f.GetType().Name;
            if (!(f is LenSync.UI.Dialogs.CardWindow) || !f.Visible || f.IsDisposed) continue;
            if (!_seen.ContainsKey(f)) _seen[f] = DateTime.Now;
            if ((DateTime.Now - _seen[f]).TotalMilliseconds < 500) continue;   // let the open animation finish
            if (_queue.Count > 0)
            {
                var item = _queue[0];
                _queue.RemoveAt(0);
                bool done;
                try { done = item.handle(f); }
                catch (Exception ex) { Unexpected.Add($"bot handler '{item.what}' threw {ex.Message}"); done = true; }
                if (done) { _seen.Remove(f); return; }
                _queue.Insert(0, item);
            }
            // A message box nobody expected (e.g. an error) that has waited 2.5 s: record it and close it.
            if (tn == "LsDialogWindow" && (DateTime.Now - _seen[f]).TotalMilliseconds > 2500)
            {
                Unexpected.Add($"{H.Field(f, "_title")}: {H.Field(f, "_message")}");
                H.ClickLater(H.Field<Control>(f, "_primary"));
                _seen.Remove(f);
                return;
            }
        }
    }

    // ---- common handlers
    public static Func<Form, bool> Message(string titleContains, bool yes, Action<Form> inspect = null) => f =>
    {
        if (f.GetType().Name != "LsDialogWindow" || !((string)H.Field(f, "_title")).Contains(titleContains)) return false;
        inspect?.Invoke(f);
        H.ClickLater(H.Field<Control>(f, yes ? "_primary" : "_secondary"));
        return true;
    };

    public static Func<Form, bool> Reason(string text, Action<Form> inspect = null) => f =>
    {
        if (f.GetType().Name != "ReasonDialog") return false;
        inspect?.Invoke(f);
        H.Field<Control>(f, "_txtReason").Text = text;
        H.ClickLater(H.Field<Control>(f, "PrimaryButton"));
        return true;
    };
}

static class Program
{
    static Bot bot;
    static int adminId;

    [STAThread]
    static int Main(string[] args)
    {
        H.Out = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
        Directory.CreateDirectory(H.Out);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        var uiErrors = new List<string>();
        Application.ThreadException += (s, e) => { uiErrors.Add(e.Exception.ToString()); Console.WriteLine("UI EXCEPTION " + e.Exception); };
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        var host = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000), Size = new Size(10, 10) };
        host.Shown += async (s, e) =>
        {
            try { await RunAll(); }
            catch (Exception ex) { H.Check("FATAL", "-", "harness crashed", false, ex.ToString()); }
            foreach (var u in uiErrors) H.Check("UIEX", "REL", "unhandled UI exception", false, u);
            foreach (var u in bot?.Unexpected ?? new List<string>()) H.Check("DLG", "USA", "unexpected dialog", false, u);
            host.Close();
        };
        Application.Run(host);

        File.WriteAllLines(Path.Combine(H.Out, "results.csv"),
            new[] { "id,area,result,name,detail" }.Concat(H.Results.Select(r => string.Join(",", r.Select(x => "\"" + x.Replace("\"", "'").Replace("\r", " ").Replace("\n", " ") + "\"")))));
        var fails = H.Results.Count(r => r[2] == "FAIL");
        Console.WriteLine($"TOTAL {H.Results.Count}  PASS {H.Results.Count - fails}  FAIL {fails}");
        return fails == 0 ? 0 : 1;
    }

    static Task Wait(int ms) => Task.Delay(ms);

    static async Task WaitFor(Func<bool> cond, int timeoutMs = 6000)
    {
        var sw = Stopwatch.StartNew();
        while (!cond() && sw.ElapsedMilliseconds < timeoutMs) await Task.Delay(100);
    }

    static async Task RunAll()
    {
        bot = new Bot();
        Logic();
        await Ui();
        await CashierUi();
    }

    // =============================================================================================== logic
    static Booking BookingByNo(string suffix) =>
        BookingAdmin.List(BookingFilter.All).First(b => b.BookingNo.EndsWith(suffix));

    static AdminResult Bill(Booking b, decimal? expectedOverride = null, string method = "Cash", decimal tendered = 0m)
    {
        var start = Billing.StartBill(b.Id);
        var lines = Billing.InitialLines(start.Booking);
        var t = Billing.Compute(lines, null, start.Booking.Downpayment, start.TaxRate);
        var req = new SaleRequest
        {
            BookingId = b.Id, Lines = lines, PaymentMethod = method, ReferenceNo = method == "Cash" ? "" : "GC12345678",
            AmountTendered = tendered > 0 ? tendered : t.BalanceDue, ExpectedBalance = expectedOverride ?? t.BalanceDue
        };
        return Billing.CompleteSale(req);
    }

    static void Logic()
    {
        var sign = Auth.SignIn("admin", "Admin@123");
        if (!H.Check("L00", "SEC", "admin signs in on lensync_test", sign.Status == SignInStatus.Success, sign.Status.ToString())) return;
        adminId = Session.CurrentUserId;

        // ---- #2 tax
        H.Try("L01", "FS", "tax: 500,000 at 6% = 30,000; total 530,000", () =>
        {
            var lines = new List<BillLine> { new BillLine { ItemType = ItemTypes.Package, Description = "P", Qty = 1, UnitPrice = 500000m } };
            var t = Billing.Compute(lines, null, 0m, 6m);
            return t.TaxAmount == 30000m && t.Total == 530000m ? $"tax {t.TaxAmount} total {t.Total}" : $"FAIL tax {t.TaxAmount} total {t.Total}";
        });
        H.Try("L02", "FS", "tax is on the discounted amount; downpayment comes off after tax", () =>
        {
            var lines = new List<BillLine> { new BillLine { ItemType = ItemTypes.Package, Description = "P", Qty = 1, UnitPrice = 500000m } };
            var t = Billing.Compute(lines, new Discount { Kind = DiscountTypes.Percent, Value = 10m, Reason = "x" }, 50000m, 6m);
            return t.TaxAmount == 27000m && t.Total == 477000m && t.BalanceDue == 427000m ? "27,000 / 477,000 / 427,000" : $"FAIL {t.TaxAmount} {t.Total} {t.BalanceDue}";
        });
        H.Try("L03", "FS", "StartBill carries the studio tax rate (6)", () =>
        {
            var s = Billing.StartBill(BookingByNo("0001").Id);
            return s.TaxRate == 6m ? "6" : "FAIL " + s.TaxRate;
        });
        H.Try("L04", "FS", "completed sale stores tax_rate/tax_amount and total incl. tax (BK-0001: 2,500 + 6% - 500 dp)", () =>
        {
            var r = Bill(BookingByNo("0001"));
            if (!r.Ok) return "FAIL " + string.Join("; ", r.Errors.Select(e => e.Value));
            var id = ((SaleOutcome)r.Value).TransactionId;
            var row = Db.Query("SELECT tax_rate, tax_amount, total, balance_due FROM transactions WHERE id = @id", Db.P("@id", id)).Rows[0];
            var ok = (decimal)row["tax_rate"] == 6m && (decimal)row["tax_amount"] == 150m && (decimal)row["total"] == 2650m && (decimal)row["balance_due"] == 2150m;
            var receipt = Billing.LoadReceipt(id);
            ok &= receipt.TaxAmount == 150m && receipt.TaxRate == 6m;
            return (ok ? "" : "FAIL ") + $"rate {row["tax_rate"]} tax {row["tax_amount"]} total {row["total"]} balance {row["balance_due"]}";
        });
        H.Try("L05", "REL", "tax rate changed while the bill was open -> sale refused", () =>
        {
            var b = BookingByNo("0002");
            var s = Billing.StartBill(b.Id);
            var expected = Billing.Compute(Billing.InitialLines(s.Booking), null, s.Booking.Downpayment, s.TaxRate).BalanceDue;
            AppSettings.SetValue(AppSettings.TaxRate, "12.00");
            try
            {
                var r = Bill(b, expected);
                return !r.Ok && r.Errors.Any(e => e.Value.Contains("tax rate")) ? r.Errors[0].Value : "FAIL saved anyway";
            }
            finally { AppSettings.SetValue(AppSettings.TaxRate, "6.00"); }
        });
        H.Try("L06", "FS", "tax rate 0 -> no tax (BK-0002 GCash)", () =>
        {
            AppSettings.SetValue(AppSettings.TaxRate, "0");
            try
            {
                var r = Bill(BookingByNo("0002"), method: "GCash");
                if (!r.Ok) return "FAIL " + r.Errors[0].Value;
                var id = ((SaleOutcome)r.Value).TransactionId;
                var tax = H.Dec("SELECT tax_amount FROM transactions WHERE id=@id", "@id", id);
                var total = H.Dec("SELECT total FROM transactions WHERE id=@id", "@id", id);
                return tax == 0m && total == 1200m ? "tax 0, total 1,200" : $"FAIL tax {tax} total {total}";
            }
            finally { AppSettings.SetValue(AppSettings.TaxRate, "6.00"); }
        });

        // ---- #4 void in Billing
        H.Try("L07", "FS", "VoidBill on an Unpaid booking -> Voided with reason, closer, time and audit", () =>
        {
            var b = BookingByNo("0003");
            var r = Billing.VoidBill(b.Id, "Customer declined the photos");
            if (!r.Ok) return "FAIL " + r.Errors[0].Value;
            var row = Db.Query("SELECT status, closed_reason, closed_by, closed_at FROM bookings WHERE id=@id", Db.P("@id", b.Id)).Rows[0];
            var audit = H.Str("SELECT details FROM audit_logs WHERE action='VOID' AND entity='bookings' AND entity_id=@id ORDER BY id DESC LIMIT 1", "@id", b.Id);
            var ok = (string)row["status"] == "Voided" && (string)row["closed_reason"] == "Customer declined the photos" &&
                     Convert.ToInt32(row["closed_by"]) == adminId && row["closed_at"] != DBNull.Value && audit != null && audit.Contains("voided before payment");
            return (ok ? "" : "FAIL ") + $"{row["status"]} / {row["closed_reason"]} / audit: {audit}";
        });
        H.Try("L08", "REL", "VoidBill refuses: already voided, Pending, Paid, short reason", () =>
        {
            var again = Billing.VoidBill(BookingByNo("0003").Id, "again please");
            var pending = Billing.VoidBill(BookingByNo("0005").Id, "not allowed");
            var paid = Billing.VoidBill(BookingByNo("0001").Id, "not allowed");
            var shortR = Billing.VoidBill(BookingByNo("0004").Id, "x");
            var ok = !again.Ok && !pending.Ok && !paid.Ok && !shortR.Ok && shortR.Errors[0].Key == "reason";
            return (ok ? "" : "FAIL ") + string.Join(" | ", new[] { again, pending, paid, shortR }.Select(x => x.Ok ? "OK?!" : x.Errors[0].Value));
        });
        H.Try("L09", "REL", "a voided booking can't be billed and leaves the Billing list", () =>
        {
            var b = BookingByNo("0003");
            var r = Bill(b);
            var inList = Billing.ListUnpaid().Any(x => x.Id == b.Id);
            return !r.Ok && !inList ? r.Errors[0].Value : $"FAIL billed={r.Ok} inList={inList}";
        });
        H.Try("L10", "FS", "Bookings > Voided lists it with the reason", () =>
        {
            var v = BookingAdmin.List(BookingFilter.Voided);
            return v.Count == 1 && v[0].ClosedReason == "Customer declined the photos" && v[0].ClosedAt.HasValue ? v[0].BookingNo : "FAIL " + v.Count;
        });

        // ---- #1 cancel reason
        H.Try("L11", "FS", "cancel requires a reason; the reason is stored and listed under Cancelled", () =>
        {
            var b = BookingByNo("0006");
            var empty = BookingAdmin.Cancel(b.Id, "  ");
            var r = BookingAdmin.Cancel(b.Id, "Client moved the shoot abroad");
            var listed = BookingAdmin.List(BookingFilter.Cancelled).FirstOrDefault(x => x.Id == b.Id);
            var notes = H.Str("SELECT IFNULL(notes,'') FROM bookings WHERE id=@id", "@id", b.Id);
            var ok = !empty.Ok && r.Ok && listed?.ClosedReason == "Client moved the shoot abroad" && !notes.Contains("Cancelled:");
            return (ok ? "" : "FAIL ") + $"empty->{(empty.Ok ? "accepted" : empty.Errors[0].Value)}; listed reason '{listed?.ClosedReason}'; notes '{notes}'";
        });
        H.Try("L12", "FS", "seed cancellation has its reason (fresh install)", () =>
        {
            var c = BookingAdmin.List(BookingFilter.Cancelled).FirstOrDefault(x => x.BookingNo.EndsWith("0008"));
            return c != null && c.ClosedReason.Length > 0 ? c.ClosedReason : "FAIL no reason";
        });

        // ---- #6/#8 dashboard
        H.Try("L13", "FS", "dashboard: 7 / 8 / 6 bars, last bar contains today, revenue = today's completed totals", () =>
        {
            var parts = new List<string>();
            var ok = true;
            var todayTotal = H.Dec("SELECT IFNULL(SUM(total),0) FROM transactions WHERE status='Completed' AND created_at >= CURDATE()");
            foreach (var p in new[] { SalesPeriod.Daily, SalesPeriod.Weekly, SalesPeriod.Monthly })
            {
                var s = DashboardService.Load(p);
                var expected = p == SalesPeriod.Daily ? 7 : p == SalesPeriod.Weekly ? 8 : 6;
                ok &= s.Buckets.Count == expected && s.Buckets.Last().Contains(DateTime.Today) && s.Today.Total == todayTotal &&
                      s.Buckets.Last().Total == (p == SalesPeriod.Daily ? todayTotal : s.Buckets.Last().Total) && s.Buckets.Sum(b => b.Total) >= todayTotal;
                parts.Add($"{p}:{s.Buckets.Count} bars, last {s.Buckets.Last().Total}");
            }
            return (ok ? "" : "FAIL ") + string.Join("; ", parts) + $"; today {todayTotal}";
        });
        H.Try("L14", "FS", "Awaiting payment = Unpaid bookings; voided/cancelled not on today's schedule", () =>
        {
            var s = DashboardService.Load(SalesPeriod.Daily);
            var unpaid = (int)H.Dec("SELECT COUNT(*) FROM bookings WHERE status='Unpaid'");
            var voidedToday = s.Schedule.Any(b => b.Status == "Voided" || b.Status == "Cancelled");
            return s.AwaitingPayment == unpaid && !voidedToday ? $"awaiting {s.AwaitingPayment}, schedule {s.Schedule.Count}" : $"FAIL awaiting {s.AwaitingPayment} vs {unpaid}, voided on schedule {voidedToday}";
        });

        // ---- reports (tax + voided bills, verify removed)
        H.Try("L15", "FS", "every report builds; summary has Tax collected and Voided bills", () =>
        {
            var problems = new List<string>();
            foreach (var t in ReportTypes.All)
            {
                var d = ReportService.Build(t, DateTime.Today.AddDays(-30), DateTime.Today);
                var keys = d.Summary.Select(k => k.Key).ToList();
                if (!keys.Contains("Tax collected") || !keys.Contains("Voided bills")) problems.Add(t + " summary " + string.Join("/", keys));
            }
            return problems.Count == 0 ? "5 reports" : "FAIL " + string.Join("; ", problems);
        });
        H.Try("L16", "FS", "daily report: tax column sums to Tax collected; net = gross - discounts + tax; voided bills = 1", () =>
        {
            var d = ReportService.Build(ReportType.DailySales, DateTime.Today, DateTime.Today);
            var tbl = d.Tables[0];
            decimal M(string s) => decimal.Parse(s.Replace("₱", "").Replace(",", ""), System.Globalization.CultureInfo.InvariantCulture);
            var tot = tbl.Totals;
            var gross = M(tot[2]); var disc = M(tot[3]); var tax = M(tot[4]); var net = M(tot[5]);
            var sumTax = M(d.Summary.First(k => k.Key == "Tax collected").Value);
            var voided = d.Summary.First(k => k.Key == "Voided bills").Value;
            var ok = tax == sumTax && net == gross - disc + tax && net == d.NetSales && voided == "1" && tbl.Columns[4].Header == "Tax (₱)";
            return (ok ? "" : "FAIL ") + $"gross {gross} disc {disc} tax {tax} net {net} summaryTax {sumTax} voided {voided}";
        });
        H.Try("L17", "SEC", "issuing a report still records number + hash + code internally", () =>
        {
            var d = ReportService.Build(ReportType.Transactions, DateTime.Today, DateTime.Today);
            ReportService.Issue(d, "Harness printer");
            var code = H.Str("SELECT verification_code FROM report_logs WHERE report_no=@n", "@n", d.ReportNo);
            return d.ReportNo.StartsWith("RPT-") && code?.Length == 10 ? d.ReportNo : "FAIL " + d.ReportNo + " " + code;
        });
        H.Try("L18", "FS", "Verify is gone from the code (no ReportService.Verify, no VerifyPage)", () =>
        {
            var asm = typeof(ReportService).Assembly;
            var gone = typeof(ReportService).GetMethod("Verify") == null && asm.GetType("LenSync.Modules.Reports.VerifyPage") == null &&
                       asm.GetType("LenSync.UI.Controls.VerifyResultCard") == null;
            return gone ? "removed" : "FAIL still present";
        });

        // ---- tax setting
        H.Try("L19", "FS", "Settings > Studio: tax rate loads, saves (audited) and rejects bad input", () =>
        {
            var info = StudioAdmin.LoadStudio();
            var loaded = info.TaxRateText;
            info.TaxRateText = "abc";
            var bad = StudioAdmin.SaveStudio(info, null, null);
            info.TaxRateText = "12.5";
            var good = StudioAdmin.SaveStudio(info, null, null);
            var stored = AppSettings.GetValue(AppSettings.TaxRate);
            var audit = H.Str("SELECT details FROM audit_logs WHERE action='STUDIO_UPDATE' ORDER BY id DESC LIMIT 1");
            info.TaxRateText = "6";
            StudioAdmin.SaveStudio(info, null, null);
            var ok = loaded == "6" && !bad.Ok && bad.Errors[0].Key == "taxRate" && good.Ok && stored == "12.50" && audit.Contains("tax rate 6% -> 12.5%");
            return (ok ? "" : "FAIL ") + $"loaded '{loaded}', bad -> {(bad.Ok ? "accepted" : bad.Errors[0].Value)}, stored '{stored}', audit '{audit}'";
        });

        // ---- #3 receipt size
        H.Try("L20", "FS", "receipt height follows content and never exceeds half letter", () =>
        {
            var full = Billing.LoadReceipt((int)H.Dec("SELECT MIN(id) FROM transactions"));
            var h1 = LenSync.Printing.InvoicePrinter.PageHeightFor(full);
            for (int i = 0; i < 11; i++) full.Lines.Add(new BillLine { Description = "A very long add-on description that should be clipped " + i, Qty = 999, UnitPrice = 99999m });
            full.DiscountAmount = 10m; full.DiscountReason = "Loyal"; full.DiscountType = "Fixed"; full.Status = "Voided"; full.VoidReason = "legacy"; full.VoidedAt = DateTime.Now;
            var h12 = LenSync.Printing.InvoicePrinter.PageHeightFor(full);
            return h1 < 400 && h12 <= 612 && h12 > h1 ? $"1 line {h1}pt, 12 lines+everything {h12}pt" : $"FAIL {h1} / {h12}";
        });

        // ---- data for the UI part: one booking per flow, created here so the flows never depend on each other
        var pkg = BookingAdmin.ActivePackages().First(p => p.Name == "Family Portrait");
        int Make(string name, bool done)
        {
            var r = BookingAdmin.Save(new Booking
            {
                CustomerName = name, ContactNo = "0917 555 0101", PackageId = pkg.Id,
                ScheduleAt = DateTime.Today.AddDays(2).AddHours(10), Downpayment = 500m, DownpaymentMethod = "Cash"
            }, done);
            if (!r.Ok) throw new Exception("seed booking: " + r.Errors[0].Value);
            return (int)r.Value;
        }
        idCancelUi = Make("UI Cancel Pending", false);
        idVoidTab = Make("UI Void From Tab", true);
        idVoidDialog = Make("UI Void In Bill", true);
        idPay = Make("UI Pay Cash", true);
    }

    static int idCancelUi, idVoidTab, idVoidDialog, idPay;

    static bool SelectId(LsGrid g, int id, string what)
    {
        g.SelectWhere(o => ((Booking)o).Id == id);
        var ok = (g.SelectedItem as Booking)?.Id == id;
        if (!ok) H.Check("SEL", "-", "harness could not select " + what, false);
        return ok;
    }

    static string NameOf(Control page, Control c)
    {
        for (var t = page.GetType(); t != null; t = t.BaseType)
            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                if (ReferenceEquals(f.GetValue(page), c)) return f.Name;
        return c.GetType().Name + (string.IsNullOrEmpty(c.Text) ? "" : "'" + c.Text + "'");
    }

    /// <summary>Visible child controls of a page that overlap each other or stick out of the page.</summary>
    static List<string> LayoutProblems(Control page)
    {
        var cs = page.Controls.Cast<Control>().Where(c => c.Visible && c.Width > 0 && c.Height > 0 && !(c is Label l && l.Text.Length == 0)).ToList();
        var probs = new List<string>();
        for (int i = 0; i < cs.Count; i++)
        {
            for (int j = i + 1; j < cs.Count; j++)
                if (cs[i].Bounds.IntersectsWith(cs[j].Bounds)) probs.Add($"{NameOf(page, cs[i])} overlaps {NameOf(page, cs[j])}");
            var b = cs[i].Bounds;
            if (b.Left < 0 || b.Top < 0 || b.Right > page.ClientSize.Width + 1 || b.Bottom > page.ClientSize.Height + 1)
                probs.Add($"{NameOf(page, cs[i])} outside the page ({b} in {page.ClientSize})");
        }
        return probs;
    }

    static async Task LayoutSweep(string size)
    {
        var pages = new (string key, int tab, string name)[]
        {
            (Permissions.AddService, 0, "Bookings"), (Permissions.AddService, 1, "Billing"), (Permissions.AddService, 2, "Sales"),
            (Permissions.Reports, 0, "Create report"), (Permissions.Reports, 1, "History"),
            (Permissions.Settings, 0, "Users"), (Permissions.Settings, 1, "Roles"), (Permissions.Settings, 2, "Packages"),
            (Permissions.Settings, 3, "Add-ons"), (Permissions.Settings, 4, "Studio"), (Permissions.Settings, 5, "Email"),
        };
        foreach (var p in pages)
        {
            var page = await OpenPage<ModulePage>(p.key, p.tab);
            if (p.name == "Create report") { await WaitFor(() => H.Field(page, "_data") != null, 8000); await Wait(300); }
            H.Snap(main, $"sweep-{size}-{p.name.Replace(' ', '-').ToLower()}");
            var probs = page == null ? new List<string> { "page not found" } : LayoutProblems(page);
            H.Check($"LAY-{size}-{p.name}", "USA", $"{p.name} at {size}: nothing overlaps or sticks out", probs.Count == 0, string.Join("; ", probs));
        }
        ((IModuleHost)main).OpenModule(Permissions.Dashboard, 0);
        await Wait(1500);
        var dash = H.Tree(main).OfType<DashboardView>().First();
        H.Snap(main, $"sweep-{size}-dashboard");
        var dprobs = LayoutProblems(dash);
        H.Check($"LAY-{size}-Dashboard", "USA", $"Dashboard at {size}: nothing overlaps or sticks out", dprobs.Count == 0, string.Join("; ", dprobs));
    }

    // =============================================================================================== UI (admin)
    static Form main;

    static async Task<T> OpenPage<T>(string key, int tab) where T : Control
    {
        ((IModuleHost)main).OpenModule(key, tab);
        await Wait(700);
        var view = H.Field<Control>(main, "_current");
        if (view is TabbedView tv) { tv.SelectTab(tab); await Wait(900); }
        return H.Tree(main).OfType<T>().FirstOrDefault(c => c.Visible);
    }

    static async Task Ui()
    {
        main = new MainForm();
        main.WindowState = FormWindowState.Normal;
        main.StartPosition = FormStartPosition.Manual;
        main.Location = new Point(-3000, 40);
        main.Size = new Size(1032, 594);
        main.Show();
        main.WindowState = FormWindowState.Normal;
        main.Size = new Size(1032, 594);
        await Wait(1500);
        Console.WriteLine($"DPI scale {LenSync.UI.Theme.DpiScale}, main {main.Width}x{main.Height}");

        // ---- Dashboard (#6, #8)
        var dash = H.Tree(main).OfType<DashboardView>().FirstOrDefault();
        await WaitFor(() => H.Field(dash, "_snap") != null);
        H.Try("U01", "USA", "dashboard shows 3 tiles: Today's revenue, Shoots today, Awaiting payment", () =>
        {
            var tiles = H.Tree(dash).OfType<StatTile>().Where(t => t.Visible).Select(t => t.Text).ToList();
            H.Snap(main, "ui-dashboard-daily");
            return tiles.SequenceEqual(new[] { "Today's revenue", "Shoots today", "Awaiting payment" }) ? string.Join(", ", tiles) : "FAIL " + string.Join(", ", tiles);
        });
        var period = H.Field<LsSelect>(dash, "_period");
        foreach (var (i, name, bars) in new[] { (1, "Weekly", 8), (2, "Monthly", 6), (0, "Daily", 7) })
        {
            period.SelectedIndex = i;
            await WaitFor(() => ((DashboardSnapshot)H.Field(dash, "_snap")).Period.ToString() == name);
            await Wait(400);
            H.Try("U02" + name[0], "USA", $"dashboard filter {name} -> {bars} bars and the matching title", () =>
            {
                var snap = (DashboardSnapshot)H.Field(dash, "_snap");
                var chart = H.Field<SalesChart>(dash, "_chart");
                var chartBars = ((System.Collections.IList)H.Field(chart, "_bars")).Count;
                H.Snap(main, "ui-dashboard-" + name.ToLower());
                var sel = period.Bounds; var card = (Rectangle)H.Field(dash, "_chartCard");
                var inHeader = card.Contains(sel) && sel.Bottom <= card.Top + LenSync.UI.Theme.Dpi(48);
                return snap.Period.ToString() == name && chartBars == bars && inHeader ? $"{chartBars} bars, select in card header" : $"FAIL period {snap.Period} bars {chartBars} inHeader {inHeader}";
            });
        }

        // ---- Bookings (#1)
        var bookings = await OpenPage<BookingsPage>(Permissions.AddService, 0);
        var filter = H.Field<LsChoice>(bookings, "_filter");
        var grid = H.Field<LsGrid>(bookings, "_grid");
        foreach (var (idx, name) in new[] { (2, "Cancelled"), (3, "Voided"), (0, "Open") })
        {
            filter.SelectedIndex = idx;
            await Wait(900);
            H.Try("U03" + name[0], "USA", $"Bookings > {name}: reason column {(idx == 0 ? "hidden" : "shows the reason")}", () =>
            {
                H.Snap(main, "ui-bookings-" + name.ToLower());
                var reasonVisible = grid.Columns["reason"].Visible;
                if (idx == 0) return !reasonVisible && grid.Columns["dp"].Visible ? $"{grid.Rows.Count} open" : "FAIL reason column on Open";
                var cells = grid.Rows.Cast<DataGridViewRow>().Select(r => Convert.ToString(r.Cells["reason"].Value)).ToList();
                return reasonVisible && !grid.Columns["dp"].Visible && cells.Count > 0 && cells.All(c => c.Length > 1 && c != "—")
                    ? string.Join(" | ", cells) : $"FAIL visible {reasonVisible} cells {string.Join("|", cells)}";
            });
        }
        H.Try("U04", "USA", "Bookings filter shows 5 options incl. Voided and fits", () =>
        {
            var items = (System.Collections.IList)H.Field(filter, "_items");
            var labels = items.Cast<object>().Select(o => o.ToString()).ToList();
            // On a small window the filter wraps under the search box; it must just stay inside and clear of the buttons.
            var buttons = new[] { "_btnEdit", "_btnNext", "_btnCancel" }.Select(n => H.Field<Control>(bookings, n).Bounds);
            var fits = filter.Right <= bookings.ClientSize.Width && !buttons.Any(b => b.IntersectsWith(filter.Bounds));
            return labels.Count == 5 && labels.Contains("Voided") && fits ? string.Join("/", labels) : $"FAIL {string.Join("/", labels)} fits {fits}";
        });
        // Cancel flow through the UI: reason dialog says required
        filter.SelectedIndex = 0; await Wait(900);
        SelectId(grid, idCancelUi, "cancel booking");
        var toCancel = (Booking)grid.SelectedItem;
        bot.Expect("cancel reason (empty first)", f =>
        {
            if (f.GetType().Name != "ReasonDialog") return false;
            H.Snap(f, "ui-cancel-reason");
            var lbl = H.Tree(f).OfType<Label>().Select(l => l.Text).FirstOrDefault(t => t.StartsWith("Reason"));
            H.Check("U05", "USA", "cancel dialog labels the reason as required", lbl == "Reason / remarks (required)", lbl);
            H.ClickLater(H.Field<Control>(f, "PrimaryButton"));   // empty -> must stay open with an error
            return true;
        });
        bot.Expect("cancel reason (fill)", Bot.Reason("Customer has a family emergency", f =>
            H.Check("U06", "USA", "empty reason keeps the dialog open with an inline error", LenSync.UI.Dialogs.Validator.HasErrors(H.Field<Control>(f, "Body")))));
        H.Click(H.Field<Control>(bookings, "_btnCancel"));
        await WaitFor(() => bot.Pending == 0, 8000); await Wait(1200);
        H.Try("U07", "FS", "cancel through the UI stores the typed reason", () =>
        {
            var r = H.Str("SELECT closed_reason FROM bookings WHERE id=@id", "@id", toCancel.Id);
            return r == "Customer has a family emergency" ? toCancel.BookingNo + ": " + r : "FAIL " + r;
        });

        // ---- Billing tab (#4)
        var billing = await OpenPage<BillingPage>(Permissions.AddService, 1);
        var bGrid = H.Field<LsGrid>(billing, "_grid");
        var btnVoid = H.Field<LsButton>(billing, "_btnVoid");
        H.Try("U08", "USA", "Billing tab shows Void bill (admin) next to Bill this booking", () =>
        {
            H.Snap(main, "ui-billing");
            var bill = H.Field<LsButton>(billing, "_btnBill");
            return btnVoid.Visible && btnVoid.Top == bill.Top && btnVoid.Right < bill.Left ? $"{bGrid.Rows.Count} unpaid" : $"FAIL visible {btnVoid.Visible}";
        });
        SelectId(bGrid, idVoidTab, "void-from-tab booking");
        var voidTarget = (Booking)bGrid.SelectedItem;
        bot.Expect("void reason from Billing tab", Bot.Reason("Shoot was a free retake", f => H.Snap(f, "ui-void-reason")));
        H.Click(btnVoid);
        await WaitFor(() => bot.Pending == 0, 8000); await Wait(1200);
        H.Try("U09", "FS", "Void bill from the Billing tab voids it and removes it from the list", () =>
        {
            var st = H.Str("SELECT status FROM bookings WHERE id=@id", "@id", voidTarget.Id);
            var still = bGrid.Rows.Cast<DataGridViewRow>().Any(r => ((Booking)r.Tag).Id == voidTarget.Id);
            return st == "Voided" && !still ? voidTarget.BookingNo + " voided" : $"FAIL {st}, still listed {still}";
        });

        // Billing dialog: tax row + Void bill inside the bill
        SelectId(bGrid, idVoidDialog, "void-in-bill booking");
        var dlgTarget = (Booking)bGrid.SelectedItem;
        bot.Expect("billing dialog: check, then Void bill...", f =>
        {
            if (f.GetType().Name != "BillingDialog") return false;
            H.Snap(f, "ui-billing-dialog");
            var summary = H.Field<BillSummary>(f, "_summary");
            var totals = (BillTotals)H.Field(summary, "_totals");
            var v = H.Field<LsButton>(f, "_btnVoid");
            H.Check("U10", "FS", "bill shows tax at 6% and the total includes it", totals.TaxRate == 6m && totals.Total == totals.Subtotal + totals.TaxAmount,
                $"subtotal {totals.Subtotal} tax {totals.TaxAmount} total {totals.Total} balance {totals.BalanceDue}");
            H.Check("U11", "USA", "Void bill... is inside the bill (before payment)", v.Visible && v.Enabled, v.Text);
            H.ClickLater(v);
            return true;
        });
        bot.Expect("void reason inside bill", Bot.Reason("Duplicate booking"));
        H.Click(H.Field<LsButton>(billing, "_btnBill"));
        await WaitFor(() => bot.Pending == 0, 10000); await Wait(1500);
        H.Try("U12", "FS", "voiding inside the bill closes it and voids the booking (no receipt)", () =>
        {
            var st = H.Str("SELECT status FROM bookings WHERE id=@id", "@id", dlgTarget.Id);
            var tx = H.Dec("SELECT COUNT(*) FROM transactions WHERE booking_id=@id", "@id", dlgTarget.Id);
            var open = Application.OpenForms.Cast<Form>().Any(f => f.GetType().Name == "BillingDialog");
            return st == "Voided" && tx == 0 && !open ? "voided, 0 receipts" : $"FAIL {st} tx {tx} dialogOpen {open}";
        });

        // Bill + pay through the UI (receipt is compact)
        SelectId(bGrid, idPay, "pay booking");
        bot.Expect("billing dialog: pay cash", f =>
        {
            if (f.GetType().Name != "BillingDialog") return false;
            var totals = (BillTotals)H.Field(H.Field<BillSummary>(f, "_summary"), "_totals");
            H.Field<Control>(f, "_txtTendered").Text = totals.BalanceDue.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            H.ClickLater(H.Field<Control>(f, "PrimaryButton"));
            return true;
        });
        bot.Expect("confirm payment", Bot.Message("Complete this payment?", true));
        bot.Expect("receipt dialog", f =>
        {
            if (f.GetType().Name != "ReceiptDialog") return false;
            H.Snap(f, "ui-receipt-dialog");
            var pageH = (float)H.Field(f, "_pageH");
            H.Check("U13", "FS", "receipt after payment is compact (page height < half letter)", pageH < 612f, pageH + "pt");
            H.ClickLater(H.Field<Control>(f, "SecondaryButton"));
            return true;
        });
        H.Click(H.Field<LsButton>(billing, "_btnBill"));
        await WaitFor(() => bot.Pending == 0, 12000); await Wait(1200);
        H.Try("U13b", "FS", "paying through the UI marks the booking Paid with a taxed receipt", () =>
        {
            var st = H.Str("SELECT status FROM bookings WHERE id=@id", "@id", idPay);
            var tax = H.Str("SELECT tax_amount FROM transactions WHERE booking_id=@id", "@id", idPay);
            return st == "Paid" && tax == "150.00" ? "Paid, tax 150.00" : $"FAIL status {st}, tax {tax}";
        });
        if (bot.Pending > 0) { H.Check("BOT", "-", "bot handlers left over after payment", false, bot.Pending.ToString()); bot.Clear(); }

        // ---- Sales (#4: no void)
        var sales = await OpenPage<SalesPage>(Permissions.AddService, 2);
        H.Try("U14", "FS", "Sales has no Void button (paid receipts are final)", () =>
        {
            H.Snap(main, "ui-sales");
            var texts = H.Tree(sales).OfType<LsButton>().Where(b => b.Visible).Select(b => b.Text).ToList();
            return !texts.Any(t => t.IndexOf("void", StringComparison.OrdinalIgnoreCase) >= 0) ? string.Join(", ", texts) : "FAIL " + string.Join(", ", texts);
        });

        // ---- Reports (#7, #9)
        var create = await OpenPage<CreateReportPage>(Permissions.Reports, 0);
        await WaitFor(() => H.Field(create, "_data") != null, 8000); await Wait(500);
        H.Try("U15", "USA", "Reports: filters in one row on top, preview below, Print underneath", () =>
        {
            H.Snap(main, "ui-reports-create");
            Control C(string n) => H.Field<Control>(create, n);
            var top = C("_type").Top;
            var oneRow = new[] { "_period", "_from", "_to" }.All(n => C(n).Top == top);
            var ordered = C("_type").Right < C("_period").Left && C("_period").Right < C("_from").Left && C("_from").Right < C("_to").Left;
            var inside = C("_to").Right <= create.ClientSize.Width;
            var preview = C("_preview");
            var below = preview.Top > C("_to").Bottom && C("_btnPrint").Top > preview.Bottom && C("_btnPrint").Bottom <= create.ClientSize.Height;
            var fullWidth = preview.Width >= create.ClientSize.Width - 2;
            var inHeader = H.Field<FlowLayoutPanel>(create, "Actions").Controls.Contains(C("_btnPrint"));
            return oneRow && ordered && inside && below && fullWidth && !inHeader ? $"row y={top}, preview {preview.Width}x{preview.Height}" :
                $"FAIL oneRow {oneRow} ordered {ordered} inside {inside} below {below} fullWidth {fullWidth} printInHeader {inHeader}";
        });
        H.Try("U16", "USA", "summary line has no verification code wording", () =>
        {
            var s = H.Field<Label>(create, "_summary").Text;
            return s.Length > 0 && s.IndexOf("code", StringComparison.OrdinalIgnoreCase) < 0 ? s.Replace("\n", " / ") : "FAIL '" + s + "'";
        });
        H.Try("U17", "USA", "Reports has only Create report and History tabs", () =>
        {
            var view = H.Field<Control>(main, "_current");
            var tabs = (System.Collections.IList)H.Field(H.Field<Control>(view, "_tabs"), "_items");
            var names = tabs.Cast<object>().Select(o => o.ToString()).ToList();
            return names.SequenceEqual(new[] { "Create report", "History" }) ? string.Join(", ", names) : "FAIL " + string.Join(", ", names);
        });
        var history = await OpenPage<HistoryPage>(Permissions.Reports, 1);
        H.Snap(main, "ui-reports-history");

        // ---- Settings: Studio tax field, Users deactivate (#5)
        var studio = await OpenPage<StudioPage>(Permissions.Settings, 4);
        H.Try("U18", "USA", "Settings > Studio shows the tax rate field (6) beside Contact number", () =>
        {
            H.Snap(main, "ui-settings-studio");
            var tax = H.Field<Control>(studio, "_txtTax"); var contact = H.Field<Control>(studio, "_txtContact");
            return tax.Text == "6" && tax.Top == contact.Top && tax.Left > contact.Right && tax.Bottom < studio.ClientSize.Height ? "ok" : $"FAIL text '{tax.Text}' tax {tax.Bounds} contact {contact.Bounds}";
        });

        var users = await OpenPage<UsersPage>(Permissions.Settings, 0);
        var ugrid = H.Field<LsGrid>(users, "_grid");
        // A user to deactivate: create a cashier for the cashier test later.
        var cashierRole = UserAdmin.ListRoles().First(r => r.Name == "Cashier");
        var created = UserAdmin.CreateUser("Test Cashier", "test.cashier", "cashier@example.com", cashierRole.Id);
        cashierTemp = (string)created.Value;
        await users.ReloadAsync();
        ugrid.SelectWhere(o => ((UserSummary)o).Username == "test.cashier");
        var cashierId = ((UserSummary)ugrid.SelectedItem).Id;
        bot.Expect("deactivate: answer No", Bot.Message("Deactivate this account?", false, f =>
        {
            H.Snap(f, "ui-deactivate-confirm");
            var yes = H.Field<LsButton>(f, "_primary").Text; var no = H.Field<LsButton>(f, "_secondary").Text;
            var msg = (string)H.Field(f, "_message");
            H.Check("U19", "USA", "deactivate asks Yes/No ('No' is the safe default)", yes == "Yes, deactivate" && no == "No, keep active" && msg.StartsWith("Are you sure"),
                $"{yes} / {no}; focused: {(H.Field<LsButton>(f, "_secondary").Focused ? "No" : H.Field<LsButton>(f, "_primary").Focused ? "Yes" : "none")}");
        }));
        H.Click(H.Field<LsButton>(users, "_btnActive"));
        await WaitFor(() => bot.Pending == 0, 8000); await Wait(1000);
        var stillActive = H.Dec("SELECT is_active FROM users WHERE id=@id", "@id", cashierId) == 1m;
        H.Check("U20", "FS", "answering No keeps the account active", stillActive);
        ugrid.SelectWhere(o => ((UserSummary)o).Id == cashierId);
        bot.Expect("deactivate: answer Yes", Bot.Message("Deactivate this account?", true));
        H.Click(H.Field<LsButton>(users, "_btnActive"));
        await WaitFor(() => bot.Pending == 0, 8000); await Wait(1200);
        H.Check("U21", "FS", "answering Yes deactivates it", H.Dec("SELECT is_active FROM users WHERE id=@id", "@id", cashierId) == 0m);
        UserAdmin.SetActive(cashierId, true);

        // ---- layout sweep: every page at the minimum window and at a common laptop size
        bot.Clear();
        await LayoutSweep("1032x640");
        main.Size = new Size(1366, 768);
        await Wait(800);
        await LayoutSweep("1366x768");

        main.Close(); main.Dispose(); main = null;
        Auth.SignOut("harness");
    }

    static string cashierTemp;

    // =============================================================================================== UI (cashier)
    static async Task CashierUi()
    {
        var r = Auth.SignIn("test.cashier", cashierTemp);
        if (!H.Check("C00", "SEC", "cashier signs in", r.Status == SignInStatus.Success, r.Status.ToString())) return;
        H.Try("C01", "SEC", "cashier can't void (server check)", () =>
        {
            var b = BookingAdmin.List(BookingFilter.All).First(x => x.BookingNo.EndsWith("0002"));
            try { Billing.VoidBill(b.Id, "trying to void"); return "FAIL allowed"; }
            catch (UnauthorizedAccessException) { return "UnauthorizedAccessException"; }
        });
        main = new MainForm { WindowState = FormWindowState.Normal, StartPosition = FormStartPosition.Manual, Location = new Point(-3000, 40), Size = new Size(1032, 594) };
        main.Show(); main.Size = new Size(1032, 594);
        await Wait(1500);
        var billing = await OpenPage<BillingPage>(Permissions.AddService, 1);
        H.Try("C02", "SEC", "cashier's Billing tab hides Void bill", () =>
        {
            H.Snap(main, "ui-cashier-billing");
            return !H.Field<LsButton>(billing, "_btnVoid").Visible ? "hidden" : "FAIL visible";
        });
        main.Close(); main.Dispose();
        Auth.SignOut("harness");
    }
}
