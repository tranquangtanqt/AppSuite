using MeRon.Core.Models;
using MeRon.Core.Orders;

namespace MeRon.Tests;

/// <summary>Nhập đơn hàng loạt: đọc chữ dán vào (chép từ sổ ghi tay / Zalo) + ghép khách, món, chữ viết tắt.</summary>
public class BatchOrderTests
{
    private static readonly DateOnly Today = new(2026, 11, 3);

    private static readonly Customer[] Customers =
    [
        new(1, "Mỹ Ly", "706", null, null),
        new(2, "Tuấn Chung", "W1", null, null),
        new(3, "Thu Hà", "205", null, null),
        new(4, "Hà W3", "W3", null, null), // MeRon không cho trùng tên khách: khách cùng tên thường kèm toà
        new(5, "Hà W1", "W1", null, null),
        new(6, "Thảo", "1101", null, null),
    ];

    private static readonly Product[] Products =
    [
        new(10, 1, "Sữa chua uống", null, 15000, null),
        new(11, 1, "Sữa chua dẻo", null, 12000, null),
        new(12, 1, "Bún chả", null, 40000, null),
        new(13, 1, "Bún chả cá", null, 45000, null),
        new(14, 1, "Phô mai mận", null, 20000, null),
        new(15, 1, "Giò", null, 30000, null),
        new(16, 1, "Nấm", null, 5000, null),
    ];

    private static readonly Abbreviations Abbr = new(Abbreviations.DefaultText);

    private static BatchLineMatch MatchLine(string text) =>
        BatchOrderMatcher.Match(BatchOrderParser.Parse(text, Today).Lines.Single(), Customers, Products, Abbr);

    [Fact]
    public void Parses_claude_format_with_day_price_and_ck()
    {
        var text = BatchOrderParser.Parse("Ngày | 29/10\n\n706 Mỹ Ly | 3 sc uống\n1201 | 1 phần cá | 45k | CK\n", Today);

        Assert.Equal(new DateOnly(2026, 10, 29), text.Day);
        Assert.Equal(2, text.Lines.Count);
        Assert.Equal(3, text.Lines[0].Number);
        Assert.Equal(new BatchItem(3, "sc uống", null, false, "3 sc uống"), text.Lines[0].Items.Single());
        Assert.Equal(45000, text.Lines[1].Price);
        Assert.True(text.Lines[1].Paid);
        Assert.False(text.Lines[0].Paid);
    }

    [Fact]
    public void Parses_notebook_style_items_glued_quantity_plus_and_trailing_ck()
    {
        var line = BatchOrderParser.Parse("W1 Tuấn Chung: 1bún + 10 nấm + giò x2 + 1 phần cá 45k CK", Today).Lines.Single();

        Assert.Equal("W1 Tuấn Chung", line.Customer);
        Assert.True(line.Paid);
        Assert.Equal([(1L, "bún"), (10L, "nấm"), (2L, "giò"), (1L, "phần cá")], line.Items.Select(i => (i.Quantity, i.Name)).ToArray());
        Assert.Equal(45000, line.Items[3].Price);
    }

    [Theory]
    [InlineData("45k", 45000L)]
    [InlineData("1,5k", 1500L)]
    [InlineData("45.000", 45000L)]
    [InlineData("45000", 45000L)]
    [InlineData("abc", null)]
    [InlineData("0", null)]
    public void Parses_prices(string text, long? expected) => Assert.Equal(expected, BatchOrderParser.ParsePrice(text));

    [Fact]
    public void Line_without_separator_is_reported()
    {
        var match = MatchLine("706 Mỹ Ly 3 sc uống");
        Assert.False(match.CanSave);
        Assert.Contains("khách | món", match.Problems.Single());
    }

    [Fact]
    public void Matches_customer_by_apartment_and_name_and_expands_abbreviations()
    {
        var match = MatchLine("706 Mỹ Ly | 3 sc uống; 4 pm mận");

        Assert.True(match.CanSave);
        Assert.Equal(1, match.Customer!.Id);
        Assert.Equal([10L, 14L], match.Items.Select(i => i.Product!.Id).ToArray());
        Assert.Equal(3 * 15000 + 4 * 20000, match.Total);
    }

    [Fact]
    public void Apartment_number_must_match_whole_word()
    {
        Assert.Equal(6, MatchLine("1101 | 1 giò").Customer!.Id);
        Assert.Contains("Chưa có khách \"101\"", MatchLine("101 | 1 giò").Problems);
    }

    [Fact]
    public void Several_customers_with_the_same_name_must_be_chosen_unless_apartment_given()
    {
        var ambiguous = MatchLine("Hà | 1 giò");
        Assert.Null(ambiguous.Customer);
        Assert.Equal(3, ambiguous.CustomerCandidates.Count); // Thu Hà, Hà (W3), Hà (W1)
        Assert.Contains("khách khớp", ambiguous.Problems.Single());

        Assert.Equal(5, MatchLine("W1 Hà | 1 giò").Customer!.Id);
    }

    /// <summary>Tên khách thường kèm số phòng / toà ("w3 Ngọc"): chọn trong ô Chọn khách phải ghép được đúng khách đó.</summary>
    [Fact]
    public void Choosing_a_customer_whose_name_contains_digits_resolves_the_line()
    {
        Customer[] customers =
        [
            new(1, "w3 Ngọc", "w3", null, null),
            new(2, "w3 C Ngọc Thủy", "w3", null, null),
            new(3, "Ngọc Nam w3", "w3", null, null),
        ];
        BatchLineMatch Match(string text, params Customer[] extra) =>
            BatchOrderMatcher.Match(BatchOrderParser.Parse(text, Today).Lines.Single(), [.. customers, .. extra], Products, Abbr);

        var ambiguous = Match("Ngọc w3 | 1 giò");
        Assert.Null(ambiguous.Customer);
        Assert.True(ambiguous.CustomerCandidates.Count > 1);

        foreach (var chosen in customers.Where(c => ambiguous.CustomerCandidates.Contains(c)))
        {
            var rewritten = BatchOrderParser.Format(ambiguous.Line, customer: chosen.Name);
            Assert.Equal(chosen.Id, Match(rewritten).Customer?.Id);
        }

        Assert.Equal(1, Match("w3 Ngọc | 1 giò").Customer?.Id); // gõ đúng nguyên tên: khỏi phải chọn

        // Khách cùng toà tên ngắn hơn: không nhận nhầm sang nhau
        Customer shortName = new(4, "Ngọc", "w3", null, null);
        Assert.Equal(1, Match("w3 Ngọc | 1 giò", shortName).Customer?.Id);
        Assert.Equal(4, Match("Ngọc | 1 giò", shortName).Customer?.Id);
    }

    /// <summary>Nút Chọn khách: chữ không khớp ai, thay bằng khách đã có; món, giá, CK trên dòng giữ nguyên.</summary>
    [Fact]
    public void Replacing_a_wrongly_matched_customer_keeps_the_rest_of_the_line()
    {
        var matched = MatchLine("706? | 2 giò; 1 sc uống | 45k | CK");
        Assert.Equal(1, matched.Customer?.Id);

        var rewritten = BatchOrderParser.Format(matched.Line, customer: "Thu Hà");
        Assert.Equal("Thu Hà | 2 giò; 1 sc uống | 45k | CK", rewritten);

        var fixedLine = MatchLine(rewritten);
        Assert.Equal(3, fixedLine.Customer?.Id);
        Assert.False(fixedLine.Line.CustomerUnsure);
        Assert.True(fixedLine.Line.Paid);
        Assert.Equal(matched.Line.Items.Count, fixedLine.Line.Items.Count);
    }

    [Fact]
    public void Replacing_an_unknown_customer_keeps_the_rest_of_the_line()
    {
        var unknown = MatchLine("w3 Ngc? | 2 giò; 1 sc uống | 45k | CK");
        Assert.Empty(unknown.CustomerCandidates);
        Assert.Contains("Chưa có khách", unknown.Problems.Single());

        var rewritten = BatchOrderParser.Format(unknown.Line, customer: "Mỹ Ly");
        Assert.Equal("Mỹ Ly | 2 giò; 1 sc uống | 45k | CK", rewritten);

        var fixedLine = MatchLine(rewritten);
        Assert.Equal(1, fixedLine.Customer?.Id);
        Assert.False(fixedLine.Line.CustomerUnsure);
        Assert.True(fixedLine.Line.Paid);
        Assert.Equal(45000, fixedLine.Line.Price);
    }

    [Fact]
    public void Product_name_ignores_spaces_and_accents_but_must_be_unique()
    {
        Assert.Equal(14, MatchLine("706 | 1 phomai mận").Items.Single().Product!.Id);
        var bun = MatchLine("706 | 1 bún").Items.Single();
        Assert.Null(bun.Product);
        Assert.Equal(2, bun.Candidates.Count); // Bún chả, Bún chả cá
        Assert.Equal(13, MatchLine("706 | 1 bún chả cá").Items.Single().Product!.Id);
    }

    [Fact]
    public void Unknown_item_with_price_is_a_free_item_and_line_price_goes_to_the_only_unknown_item()
    {
        var inline = MatchLine("706 | 1 phần cá 45k");
        Assert.True(inline.CanSave);
        Assert.Null(inline.Items.Single().Product);
        Assert.Equal(45000, inline.Items.Single().FreePrice);

        // Người dùng chốt: món tự do tên = chữ như ghi kể cả số lượng, số lượng 1, đơn giá = giá ghi ("5 lạng tôm 100k" = 100.000).
        var shrimp = MatchLine("1101 | 5 lạng tôm 100k").Items.Single();
        Assert.Equal(("5 lạng tôm", 1L, 100000L), (shrimp.FreeName, shrimp.Quantity, shrimp.UnitPrice));

        var column = MatchLine("706 | 1 giò; 2 phần cá | 45k | CK");
        Assert.True(column.CanSave);
        Assert.Equal(30000 + 45000, column.Total);

        var draft = column.ToDraft(new DateOnly(2026, 10, 29));
        Assert.True(draft.IsPrepaid);
        Assert.Equal(new DateTime(2026, 10, 29), draft.Delivery);
        Assert.Equal(new OrderLineDraft(null, 1, true, "2 phần cá", 45000), draft.Lines[1]);
        OrderCalculator.Validate(draft); // lưu được như đơn nhập tay
    }

    [Fact]
    public void Unknown_item_without_price_blocks_saving()
    {
        var match = MatchLine("706 | 5 pama");
        Assert.False(match.CanSave);
        Assert.Contains("Món lạ \"pama\"", match.Problems.Single());
    }

    [Fact]
    public void Question_mark_is_a_warning_not_a_problem()
    {
        var match = MatchLine("706 Mỹ Ly? | 3 sc uống?");
        Assert.True(match.CanSave);
        Assert.Equal(2, match.Warnings.Count);
        Assert.Equal("Mỹ Ly", match.Customer!.Name);
    }

    [Fact]
    public void Format_rewrites_chosen_customer_and_item_and_replace_remove_lines()
    {
        var line = BatchOrderParser.Parse("Hà? | 1 bún; 2 phần cá? | 45k | CK", Today).Lines.Single();
        Assert.Equal("Hà W1 | 1 Bún chả cá; 2 phần cá? | 45k | CK", BatchOrderParser.Format(line, customer: "Hà W1", itemIndex: 0, itemName: "Bún chả cá"));

        Assert.Equal("a\nX\nc", BatchOrderParser.ReplaceLine("a\r\nb\r\nc", 2, "X"));
        Assert.Equal("a\nX\nc", BatchOrderParser.ReplaceLine("a\rb\rc", 2, "X")); // ô nhập WinUI xuống dòng bằng \r
        Assert.Equal([1, 2], BatchOrderParser.Parse("706 | 1 giò\r205 | 1 giò", Today).Lines.Select(l => l.Number).ToArray());
        Assert.Equal("Ngày | 29/10\nc", BatchOrderParser.RemoveLines("Ngày | 29/10\nb\nc", [2]));
    }

    [Fact]
    public void Same_text_ignores_the_line_break_style()
    {
        Assert.True(BatchOrderParser.SameText("a\nb", "a\rb"));
        Assert.True(BatchOrderParser.SameText("a\r\nb", "a\nb"));
        Assert.False(BatchOrderParser.SameText("a\nb", "a\nc"));
    }

    [Fact]
    public void Has_line_tells_when_the_text_moved_on_from_the_row()
    {
        var text = "706 | 1 bún\r  1101 | 2 sc  ";

        Assert.True(BatchOrderParser.HasLine(text, 2, "1101 | 2 sc"));
        Assert.False(BatchOrderParser.HasLine("Thêm dòng đầu\r" + text, 2, "1101 | 2 sc"));
        Assert.False(BatchOrderParser.HasLine(text, 3, "1101 | 2 sc"));
    }

    private static IReadOnlyList<BatchLine> Lines(string text) => BatchOrderParser.Parse(text, Today).Lines;

    [Fact]
    public void Edited_lines_pairs_a_line_changed_in_place()
    {
        var edited = BatchOrderParser.EditedLines(Lines("706 | 1 bún\n1101 | 2 sc"), Lines("706 | 1 bún\n1101 | 3 sc")).ToList();

        Assert.Equal([("1101 | 2 sc", "1101 | 3 sc")], edited);
    }

    [Theory]
    [InlineData("205 | 1 giò\n706 | 1 bún\n1101 | 2 sc")] // thêm dòng phía trên: các dòng cũ bị đẩy xuống
    [InlineData("1101 | 2 sc")] // xoá dòng phía trên
    [InlineData("706 | 1 bún\n1101 | 2 sc\n205 | 1 giò")] // thêm dòng cuối
    public void Edited_lines_ignores_lines_that_only_moved(string after)
    {
        Assert.Empty(BatchOrderParser.EditedLines(Lines("706 | 1 bún\n1101 | 2 sc"), Lines(after)));
    }

    [Fact]
    public void Abbreviations_replace_whole_words_only()
    {
        var abbr = new Abbreviations("SC = sữa chua\n bad line \nbb=bánh bao");
        Assert.Equal(2, abbr.Count);
        Assert.Equal("sữa chua uống", abbr.Expand("sc uống"));
        Assert.Equal("bánh bao scx", abbr.Expand("bb scx"));
    }

    // ---------- So khớp với đơn đã có trong MeRon cùng ngày ----------

    private static Order DbOrder(long id, long userId, OrderStatus status, params (long? ProductId, string Name, long Price, long Quantity)[] items) =>
        new(id, userId, null, status, "2026-10-29", null, 0, 0, 0, null,
            items.Select((x, i) => new OrderItem(i, id, x.ProductId, x.Name, x.Price, x.Quantity, x.Price * x.Quantity)).ToList());

    private static IReadOnlyList<ReconcileEntry> Reconcile(string text, params Order[] orders) =>
        BatchReconciler.Compare(BatchOrderParser.Parse(text, Today).Lines.Select(l => BatchOrderMatcher.Match(l, Customers, Products, Abbr)).ToList(), orders);

    [Fact]
    public void Reconcile_sorts_lines_and_orders_into_matched_different_only_in_book_and_only_in_app()
    {
        var entries = Reconcile(
            "706 Mỹ Ly | 3 sc uống\n" +                 // khớp (đơn 1)
            "W1 Tuấn Chung | 2 giò; 1 nấm | | CK\n" +   // lệch với đơn 2
            "205 Thu Hà | 1 giò\n" +                    // chưa có trong MeRon
            "999 | 1 giò",                              // chưa ghép khách
            DbOrder(1, 1, OrderStatus.Shipping, (10, "Sữa chua uống", 15000, 3)),
            DbOrder(2, 2, OrderStatus.Shipping, (15, "Giò", 30000, 1), (12, "Bún chả", 40000, 1)),
            DbOrder(3, 6, OrderStatus.Completed, (15, "Giò", 30000, 1)));  // Thảo: không có trong sổ

        Assert.Equal(ReconcileKind.NotComparable, entries.Single(e => e.Draft?.Line.Customer == "999").Kind);
        Assert.Equal(1, entries.Single(e => e.Kind == ReconcileKind.Matched).Order!.Id);
        Assert.Equal("Thu Hà", entries.Single(e => e.Kind == ReconcileKind.OnlyInDraft).Draft!.Customer!.Name);
        Assert.Equal(3, entries.Single(e => e.Kind == ReconcileKind.OnlyInDatabase).Order!.Id);

        var different = entries.Single(e => e.Kind == ReconcileKind.Different);
        Assert.Equal(2, different.Order!.Id);
        Assert.Equal(
            ["Giò: sổ 2, MeRon 1", "MeRon thiếu 1 Nấm", "MeRon thừa 1 Bún chả", "sổ ghi CK, MeRon chưa thanh toán"],
            different.Differences);
    }

    [Fact]
    public void Reconcile_pairs_identical_orders_first_when_a_customer_has_two_orders()
    {
        var entries = Reconcile(
            "706 Mỹ Ly | 1 giò\n706 Mỹ Ly | 3 sc uống",
            DbOrder(1, 1, OrderStatus.Shipping, (10, "Sữa chua uống", 15000, 3)),
            DbOrder(2, 1, OrderStatus.Shipping, (15, "Giò", 30000, 1)));

        Assert.All(entries, e => Assert.Equal(ReconcileKind.Matched, e.Kind));
        Assert.Equal(2, entries.Single(e => e.Draft!.Line.Number == 1).Order!.Id);
    }

    [Fact]
    public void Reconcile_compares_free_items_by_name_without_accents_and_price()
    {
        var same = Reconcile("706 | 1 phần cá 45k", DbOrder(1, 1, OrderStatus.Shipping, (null, "1 Phần cá", 45000, 1)));
        Assert.Equal(ReconcileKind.Matched, same.Single().Kind);

        var otherPrice = Reconcile("706 | 1 phần cá 45k", DbOrder(1, 1, OrderStatus.Shipping, (null, "1 phần cá", 50000, 1)));
        Assert.Equal(ReconcileKind.Different, otherPrice.Single().Kind);
    }

    /// <summary>Đọc lại dòng popup Sửa đơn viết ra, với bộ món riêng.</summary>
    private static BatchLineMatch Reread(string line, params Product[] products)
    {
        var parsed = BatchOrderParser.Parse(line, Today).Lines.Single();
        return BatchOrderMatcher.Match(parsed, Customers, products, new Abbreviations(Abbreviations.DefaultText));
    }

    [Fact]
    public void Compose_writes_customer_items_and_paid_then_reads_back_the_same_order()
    {
        var map = Products.ToDictionary(p => p.Id);
        var line = BatchOrderParser.Compose("Mỹ Ly", [new OrderLineDraft(10, 3), new OrderLineDraft(15, 1)], map, paid: true);

        Assert.Equal("Mỹ Ly | 3 Sữa chua uống; 1 Giò |  | CK", line);
        var match = Reread(line, Products);
        Assert.True(match.CanSave);
        Assert.Empty(match.Warnings);
        Assert.Equal(1, match.Customer!.Id);
        Assert.True(match.Line.Paid);
        Assert.Equal(75000, match.Total);
    }

    [Fact]
    public void Compose_without_paid_leaves_no_trailing_separators()
    {
        var line = BatchOrderParser.Compose("Mỹ Ly", [new OrderLineDraft(10, 3)], Products.ToDictionary(p => p.Id), paid: false);

        Assert.Equal("Mỹ Ly | 3 Sữa chua uống", line);
    }

    [Theory]
    [InlineData(1, 45000, "phần cá 45k", "phần cá", 45000)]
    [InlineData(2, 45000, "2 phần cá 90k", "2 phần cá", 90000)]
    [InlineData(1, 12500, "phần cá 12500", "phần cá", 12500)]
    [InlineData(1, 500, "phần cá 0.5k", "phần cá", 500)]
    public void Compose_free_item_keeps_the_line_total_shown_on_the_popup(long quantity, long unitPrice, string written, string name, long total)
    {
        var line = BatchOrderParser.Compose("Mỹ Ly", [new OrderLineDraft(null, quantity, true, "phần cá", unitPrice)], new Dictionary<long, Product>(), paid: false);

        Assert.Equal($"Mỹ Ly | {written}", line);
        var match = Reread(line, Products);
        var item = Assert.Single(match.Items);
        Assert.True(item.IsFree);
        Assert.Equal(name, item.FreeName);
        Assert.Equal(1, item.Quantity);
        Assert.Equal(total, match.Total);
    }

    [Fact]
    public void Compose_removes_separators_from_names()
    {
        var line = BatchOrderParser.Compose("Mỹ Ly?", [new OrderLineDraft(null, 1, true, "tôm, cá + mực | to?", 100000)], new Dictionary<long, Product>(), paid: false);

        Assert.Equal("Mỹ Ly | tôm cá mực to 100k", line);
        var match = Reread(line, Products);
        Assert.Empty(match.Warnings);
        Assert.Equal(100000, Assert.Single(match.Items).UnitPrice);
    }

    [Fact]
    public void Compose_product_name_that_ends_with_a_price_reads_back_as_that_product()
    {
        Product[] products = [new(20, 1, "Yến chưng 40k", null, 45000, null), new(21, 1, "Yến chưng 50k", null, 50000, null)];

        var line = BatchOrderParser.Compose("Mỹ Ly", [new OrderLineDraft(20, 2)], products.ToDictionary(p => p.Id), paid: false);

        Assert.Equal("Mỹ Ly | 2 Yến chưng 40k", line);
        var match = Reread(line, products);
        Assert.True(match.CanSave);
        Assert.Empty(match.Warnings);
        var item = Assert.Single(match.Items);
        Assert.Equal(20, item.Product!.Id);
        Assert.Equal(2, item.Quantity);
        Assert.Equal(90000, match.Total);
    }

    [Theory]
    [InlineData("3 xôi gấc", 3, "xôi gấc")]
    [InlineData("3 x sc", 3, "sc")]
    [InlineData("3x sc", 3, "sc")]
    [InlineData("3 × sc", 3, "sc")]
    [InlineData("3*sc", 3, "sc")]
    [InlineData("3sc", 3, "sc")]
    public void Name_starting_with_x_is_not_a_multiply_sign(string item, long quantity, string name)
    {
        var parsed = Assert.Single(BatchOrderParser.Parse($"706 | {item}", Today).Lines.Single().Items);

        Assert.Equal(quantity, parsed.Quantity);
        Assert.Equal(name, parsed.Name);
    }

    [Fact]
    public void Compose_product_name_starting_with_x_reads_back()
    {
        Product[] products = [new(30, 1, "Xôi gấc", null, 20000, null)];

        var line = BatchOrderParser.Compose("Mỹ Ly", [new OrderLineDraft(30, 3)], products.ToDictionary(p => p.Id), paid: false);

        var item = Assert.Single(Reread(line, products).Items);
        Assert.Equal(30, item.Product!.Id);
        Assert.Equal(3, item.Quantity);
    }

    [Fact]
    public void Compose_large_quantity_reads_back()
    {
        var line = BatchOrderParser.Compose("Mỹ Ly", [new OrderLineDraft(16, 1000)], Products.ToDictionary(p => p.Id), paid: false);

        var item = Assert.Single(Reread(line, Products).Items);
        Assert.Equal(16, item.Product!.Id);
        Assert.Equal(1000, item.Quantity);
    }

    // ---------- Dòng trùng trong ô chữ, ngày trang sổ qua năm (2026-11-10) ----------

    private static IReadOnlyDictionary<BatchLineMatch, int> Duplicates(string text, out IReadOnlyList<BatchLineMatch> matches)
    {
        matches = BatchOrderParser.Parse(text, Today).Lines.Select(l => BatchOrderMatcher.Match(l, Customers, Products, Abbr)).ToList();
        return BatchReconciler.DuplicateLines(matches);
    }

    [Fact]
    public void Duplicate_lines_point_to_the_first_identical_line()
    {
        var duplicates = Duplicates(
            "706 Mỹ Ly | 3 sc uống\n" +
            "W1 Tuấn Chung | 1 giò\n" +
            "706 Mỹ Ly | 3 Sữa chua uống\n" +   // cùng món, viết khác
            "706 Mỹ Ly | 1 sc uống; 2 sc uống\n" + // cộng dồn = 3
            "706 Mỹ Ly | 3 sc uống | | CK\n" +    // khác thanh toán
            "706 Mỹ Ly | 2 sc uống\n" +           // khác số lượng
            "205 Thu Hà | 3 sc uống", out var matches);

        Assert.Equal([3, 4], duplicates.Keys.Select(m => m.Line.Number).Order());
        Assert.All(duplicates.Values, first => Assert.Equal(1, first));
        Assert.Equal(7, matches.Count);
    }

    [Fact]
    public void Duplicate_lines_ignore_lines_that_cannot_be_saved_yet()
    {
        Assert.Empty(Duplicates("999 | 1 giò\n999 | 1 giò\n706 | 1 xyz\n706 | 1 xyz", out _));
    }

    // ---------- Ghim lựa chọn ở popup Sửa đơn / ô chọn trên thẻ (2026-11-13) ----------

    /// <summary>Dữ liệu cũ (app Flutter) có thể có 2 khách trùng hẳn tên - chữ dòng chỉ ghi tên nên không phân biệt được.</summary>
    private static readonly Customer[] TwinCustomers = [.. Customers, new(7, "Nhung Lê", "W3", null, null), new(8, "Nhung Lê", "W1", null, null)];

    private static BatchLineMatch MatchPinned(string text, BatchPin? pin, IReadOnlyList<Customer>? customers = null, IReadOnlyList<Product>? products = null) =>
        BatchOrderMatcher.Match(BatchOrderParser.Parse(text, Today).Lines.Single(), customers ?? TwinCustomers, products ?? Products, Abbr, pin);

    [Fact]
    public void Pinned_customer_picks_one_of_two_customers_with_the_same_name()
    {
        var unpinned = MatchPinned("Nhung Lê | 1 giò", null);
        Assert.Null(unpinned.Customer);
        Assert.False(unpinned.Pinned);

        var pinned = MatchPinned("Nhung Lê | 1 giò", BatchPin.Empty.WithCustomer(7));
        Assert.Equal(7, pinned.Customer?.Id);
        Assert.True(pinned.CanSave);
        Assert.True(pinned.Pinned);

        // Khách đã ghim bị xoá: đọc theo chữ như thường.
        var gone = MatchPinned("Nhung Lê | 1 giò", BatchPin.Empty.WithCustomer(99));
        Assert.Null(gone.Customer);
        Assert.False(gone.Pinned);
    }

    [Fact]
    public void Customer_text_adds_the_address_only_when_another_customer_has_the_same_name()
    {
        var all = TwinCustomers.Select(c => (c.Id, c.Name)).ToList();
        var twin = BatchOrderParser.CustomerText(7, "Nhung Lê", "W3", all);
        Assert.Equal("Nhung Lê W3", twin);
        Assert.Equal("Mỹ Ly", BatchOrderParser.CustomerText(1, "Mỹ Ly", "706", all));

        // Bỏ ghim vẫn đọc ra đúng khách nhờ địa chỉ.
        Assert.Equal(7, MatchPinned($"{twin} | 1 giò", null).Customer?.Id);
        Assert.Equal("Nhung Lê W3 | 1 giò", BatchOrderParser.Format(MatchPinned("Nhung Lê | 1 giò", null).Line, customer: twin));
    }

    [Fact]
    public void Pinned_product_replaces_an_ambiguous_match_and_deleted_product_falls_back()
    {
        var pinned = MatchPinned("706 | 2 bún", BatchPin.Empty.WithItem(0, 13));
        var item = Assert.Single(pinned.Items);
        Assert.Equal((13L, 2L), (item.Product!.Id, item.Quantity));
        Assert.True(pinned.CanSave);

        var gone = MatchPinned("706 | 2 bún", BatchPin.Empty.WithItem(0, 999));
        Assert.False(gone.CanSave);
        Assert.False(gone.Pinned);

        // Ghim ở vị trí không còn món (chữ bớt món) thì bỏ qua.
        Assert.False(MatchPinned("706 | 2 bún", BatchPin.Empty.WithItem(3, 13)).Pinned);
    }

    [Fact]
    public void Pinned_free_item_is_not_matched_to_a_product_with_a_similar_name()
    {
        Product[] products = [.. Products, new(40, 1, "Cá kho", null, 60000, null)];

        Assert.Equal(40, Assert.Single(MatchPinned("706 | 1 cá 45k", null, products: products).Items).Product?.Id);

        var free = Assert.Single(MatchPinned("706 | 1 cá 45k", BatchPin.Empty.WithItem(0, null), products: products).Items);
        Assert.True(free.IsFree);
        Assert.Equal(45000, free.UnitPrice);

        // Giá ở cột riêng vẫn là giá món tự do đã ghim.
        var column = Assert.Single(MatchPinned("706 | 1 cá | 45k", BatchPin.Empty.WithItem(0, null), products: products).Items);
        Assert.Equal((true, 45000L), (column.IsFree, column.UnitPrice));
    }

    /// <summary>Áp dụng popup Sửa đơn = Compose + ghim; đọc lại phải ra đúng như trên popup, kể cả khách trùng tên.</summary>
    [Fact]
    public void Popup_result_reads_back_the_same_with_its_pin()
    {
        var map = Products.ToDictionary(p => p.Id);
        OrderLineDraft[] lines = [new(13, 2), new(null, 1, true, "Giò", 50000)]; // món tự do trùng tên món "Giò"
        var text = BatchOrderParser.Compose("Nhung Lê", lines, map, paid: true);
        var pin = BatchPin.FromLines(8, lines);

        Assert.Equal("Nhung Lê | 2 Bún chả cá; Giò 50k |  | CK", text);
        var match = MatchPinned(text, pin);
        Assert.Equal(8, match.Customer?.Id);
        Assert.Equal(13, match.Items[0].Product?.Id);
        Assert.True(match.Items[1].IsFree);
        Assert.Equal(2 * 45000 + 50000, match.Total);
        Assert.True(match.Line.Paid);
        Assert.Empty(match.Problems);
    }

    [Fact]
    public void Compose_keeps_the_line_price_column_unless_a_free_item_used_it()
    {
        var map = Products.ToDictionary(p => p.Id);
        var unused = MatchLine("706 | 1 giò | 45k");
        Assert.Equal(45000, BatchOrderParser.UnusedLinePrice(unused));
        Assert.Equal("Mỹ Ly | 1 Giò | 45k | CK", BatchOrderParser.Compose("Mỹ Ly", [new OrderLineDraft(15, 1)], map, paid: true, BatchOrderParser.UnusedLinePrice(unused)));
        Assert.Equal("Mỹ Ly | 1 Giò | 45k", BatchOrderParser.Compose("Mỹ Ly", [new OrderLineDraft(15, 1)], map, paid: false, 45000));

        // Giá cột riêng đã thành giá món tự do (popup ghi giá ngay sau món) → không ghi lại kẻo thành 2 lần.
        Assert.Null(BatchOrderParser.UnusedLinePrice(MatchLine("706 | 1 giò; 2 phần cá | 45k")));
    }

    [Fact]
    public void Pins_save_load_prune_and_ignore_a_broken_file()
    {
        var folder = Path.Combine(Path.GetTempPath(), "MeRonPins_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, BatchPins.FileName);
            Assert.Equal(0, BatchPins.Load(path).Count);

            var pins = new BatchPins();
            pins.Set(" Nhung Lê | 1 giò ", BatchPin.Empty.WithCustomer(7).WithItem(0, 15));
            pins.Set("706 | 1 cá 45k", BatchPin.Empty.WithItem(0, null));
            pins.Save(path);

            var loaded = BatchPins.Load(path);
            Assert.Equal(2, loaded.Count);
            var pin = loaded.Get("Nhung Lê | 1 giò")!;
            Assert.Equal(7, pin.CustomerId);
            Assert.Equal(new BatchPinItem(0, 15), Assert.Single(pin.Items));
            Assert.Null(Assert.Single(loaded.Get("706 | 1 cá 45k")!.Items).ProductId);

            // Dòng không còn trong ô chữ thì bỏ ghim, trừ dòng đang chờ hỏi.
            Assert.True(loaded.Prune(["Nhung Lê | 1 giò"]));
            Assert.Equal(1, loaded.Count);
            Assert.False(loaded.Prune([], keep: ["Nhung Lê | 1 giò"]));
            Assert.Equal(1, loaded.Count);

            File.WriteAllText(path, "{ hỏng");
            Assert.Equal(0, BatchPins.Load(path).Count);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Day_line_without_year_takes_the_year_nearest_today()
    {
        Assert.Equal(new DateOnly(2026, 12, 30), BatchOrderParser.Parse("Ngày | 30/12\n706 | 1 giò", new DateOnly(2027, 1, 2)).Day);
        Assert.Equal(new DateOnly(2027, 1, 2), BatchOrderParser.Parse("Ngày | 2/1\n706 | 1 giò", new DateOnly(2026, 12, 30)).Day);
        Assert.Equal(new DateOnly(2026, 10, 29), BatchOrderParser.Parse("Ngày | 29/10", Today).Day);
        Assert.Equal(new DateOnly(2027, 12, 30), BatchOrderParser.Parse("Ngày | 30/12/2027", new DateOnly(2027, 1, 2)).Day);
    }
}
