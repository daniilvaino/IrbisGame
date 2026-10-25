using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using KustoGame.Server.Engines;

namespace KustoGame.Server.World;

/// <summary>
/// Deterministic generator of the game world: the "Стеклянный Маяк" company logs.
/// The content depends only on <see cref="Seed"/>; <c>today</c> only shifts calendar dates,
/// so ago()/now() keep working while every row stays the same relative to the anchor day.
/// There is intentionally no weekly seasonality (it would make content depend on the weekday).
/// </summary>
public static class WorldGenerator
{
    public const int Seed = 20240314;
    public const int Days = 30;

    /// <summary>Story facts referenced by levels and tests.</summary>
    public static class Story
    {
        public const string VictimId = "E04217";
        public const string VictimName = "Глеб Штерн";
        public const string VictimLogin = "gleb.shtern";
        public const string InsiderId = "E03981";
        public const string InsiderName = "Кирилл Лосев";
        public const string InsiderEmail = "kirill.losev@ext.mayak.corp";
        public const string AttackerIp = "198.51.100.23";
        public const string DropIp = "203.0.113.77";
        public const string PhishSender = "it-support@mayak-helpdesk.net";
        public const string PhishSubject = "Срочно: подтвердите пароль VPN";
        public const int PhishRecipients = 37;
        public const int PhishDaysAgo = 6;
        public const int LeakDaysAgo = 2;
    }

    public static GameWorld Generate(DateTime today)
    {
        today = DateTime.SpecifyKind(today.Date, DateTimeKind.Utc);
        var people = BuildEmployees(new Random(Seed));
        var ctx = new Context(today, people);

        var phish = PickPhishTargets(ctx, new Random(Seed + 1));
        return new GameWorld(today,
        [
            EmployeesTable(ctx),
            NetworkTable(ctx, phish, new Random(Seed + 2)),
            EmailTable(ctx, phish, new Random(Seed + 3)),
            BadgeTable(ctx, new Random(Seed + 4)),
            TransactionsTable(ctx, new Random(Seed + 5)),
        ]);
    }

    // ───────────────────────────── employees ─────────────────────────────

    private sealed record Person(
        int Index, string Id, string First, string Last, string Department, string Title,
        string Email, string Login, string Office, long Floor, int HireDaysAgo, bool IsContractor,
        double Score, JsonObject Profile, string Workstation, string InternalIp, string HomeIp)
    {
        public string Name => $"{First} {Last}";
        public bool Remote => Office == "Удалённо";
    }

    private sealed record Phish(IReadOnlyList<Person> Recipients, IReadOnlyList<Person> Clickers);

    private sealed class Context(DateTime today, IReadOnlyList<Person> people)
    {
        public DateTime Today { get; } = today;
        public IReadOnlyList<Person> People { get; } = people;
        public Person Victim { get; } = people.Single(p => p.Id == Story.VictimId);
        public Person Insider { get; } = people.Single(p => p.Id == Story.InsiderId);
        public DateTime At(int daysAgo, int hour, int minute, int second = 0) =>
            Today.AddDays(-daysAgo).AddHours(hour).AddMinutes(minute).AddSeconds(second);
    }

    private static readonly string[] MaleNames =
    [
        "Александр", "Алексей", "Андрей", "Антон", "Артём", "Борис", "Вадим", "Валерий", "Василий", "Виктор",
        "Виталий", "Владимир", "Владислав", "Глеб", "Григорий", "Даниил", "Денис", "Дмитрий", "Евгений", "Егор",
        "Иван", "Игорь", "Илья", "Кирилл", "Константин", "Леонид", "Максим", "Михаил", "Никита", "Николай",
        "Олег", "Павел", "Пётр", "Роман", "Руслан", "Сергей", "Станислав", "Степан", "Тимофей", "Фёдор", "Юрий", "Ярослав",
    ];

    private static readonly string[] FemaleNames =
    [
        "Александра", "Алина", "Алла", "Анастасия", "Анна", "Антонина", "Валентина", "Валерия", "Вера", "Вероника",
        "Галина", "Дарья", "Диана", "Екатерина", "Елена", "Елизавета", "Жанна", "Зоя", "Инна", "Ирина",
        "Карина", "Кира", "Ксения", "Лариса", "Любовь", "Людмила", "Маргарита", "Марина", "Мария", "Надежда",
        "Наталья", "Нина", "Оксана", "Олеся", "Ольга", "Полина", "Светлана", "София", "Татьяна", "Ульяна", "Юлия", "Яна",
    ];

    // Masculine forms; the feminine form adds "а". Лосев and Штерн are reserved for the story.
    private static readonly string[] Surnames =
    [
        "Иванов", "Смирнов", "Кузнецов", "Попов", "Васильев", "Петров", "Соколов", "Михайлов", "Новиков", "Фёдоров",
        "Морозов", "Волков", "Алексеев", "Лебедев", "Семёнов", "Егоров", "Павлов", "Козлов", "Степанов", "Николаев",
        "Орлов", "Андреев", "Макаров", "Никитин", "Захаров", "Зайцев", "Соловьёв", "Борисов", "Яковлев", "Григорьев",
        "Романов", "Воробьёв", "Сергеев", "Кузьмин", "Фролов", "Александров", "Дмитриев", "Королёв", "Гусев", "Киселёв",
        "Ильин", "Максимов", "Поляков", "Сорокин", "Виноградов", "Ковалёв", "Белов", "Медведев", "Антонов", "Тарасов",
        "Жуков", "Баранов", "Филиппов", "Комаров", "Давыдов", "Беляев", "Герасимов", "Богданов", "Осипов", "Сидоров",
        "Матвеев", "Титов", "Марков", "Миронов", "Крылов", "Куликов", "Карпов", "Власов", "Мельников", "Денисов",
        "Гаврилов", "Тихонов", "Казаков", "Афанасьев", "Данилов", "Савельев", "Тимофеев", "Фомин", "Чернов", "Абрамов",
        "Мартынов", "Ефимов", "Федотов", "Щербаков", "Назаров", "Калинин", "Исаев", "Быков", "Маслов", "Родионов",
        "Коновалов", "Лазарев", "Воронин", "Климов", "Филатов", "Голубев", "Кудрявцев", "Прохоров", "Наумов", "Потапов",
        "Журавлёв", "Трофимов", "Леонов", "Соболев", "Ермаков", "Колесников", "Гончаров", "Емельянов", "Никифоров", "Грачёв",
        "Котов", "Гришин", "Ефремов", "Архипов", "Громов", "Кириллов", "Малышев", "Панов", "Моисеев", "Румянцев",
    ];

    // Surnames that do not change with gender.
    private static readonly string[] InvariantSurnames = ["Шульц", "Коваль", "Бондаренко", "Шевченко", "Ткаченко", "Кравец", "Лившиц", "Гейне"];

    private sealed record DepartmentInfo(string Name, string Code, int Headcount, string[] Titles, string[] Skills);

    private static readonly DepartmentInfo[] Departments =
    [
        new("НИОКР Ирбис", "IRB", 180, ["Ведущий инженер", "Инженер-конструктор", "Научный сотрудник", "Техник", "Руководитель направления"], ["CAD", "матмоделирование", "испытания", "оптика", "композиты"]),
        new("Разработка", "DEV", 650, ["Разработчик", "Старший разработчик", "Тимлид", "QA-инженер", "DevOps-инженер"], ["C#", "TypeScript", "Kubernetes", "SQL", "Go", "KQL"]),
        new("ИТ", "IT", 260, ["Системный администратор", "Инженер поддержки", "Сетевой инженер", "Руководитель ИТ"], ["Windows Server", "Linux", "сети", "VPN", "виртуализация"]),
        new("Безопасность", "SEC", 60, ["Аналитик SOC", "Инженер ИБ", "Руководитель ИБ"], ["KQL", "SIEM", "форензика", "pentest"]),
        new("Логистика", "LOG", 1300, ["Диспетчер", "Логист", "Водитель-экспедитор", "Начальник смены"], ["маршрутизация", "ВЭД", "TMS", "1С"]),
        new("Склад", "WH", 900, ["Кладовщик", "Оператор погрузчика", "Старший кладовщик", "Начальник склада"], ["WMS", "погрузчик", "инвентаризация"]),
        new("Продажи", "SAL", 520, ["Менеджер по продажам", "Аккаунт-менеджер", "Руководитель отдела продаж"], ["переговоры", "CRM", "тендеры"]),
        new("Финансы", "FIN", 230, ["Бухгалтер", "Финансовый аналитик", "Казначей", "Главный бухгалтер"], ["1С", "МСФО", "Excel", "казначейство"]),
        new("Маркетинг", "MKT", 150, ["Маркетолог", "Дизайнер", "Контент-менеджер"], ["SMM", "Figma", "аналитика"]),
        new("HR", "HR", 110, ["HR-менеджер", "Рекрутер", "HR-бизнес-партнёр"], ["подбор", "адаптация", "КДП"]),
        new("Юридический", "LAW", 70, ["Юрист", "Старший юрист"], ["договоры", "арбитраж", "ВЭД"]),
        new("Поддержка клиентов", "SUP", 570, ["Оператор поддержки", "Старший оператор", "Супервайзер"], ["CRM", "скрипты", "английский"]),
    ];

    private static readonly (string Name, int Weight, long MaxFloor, string Code)[] Offices =
    [
        ("Москва", 45, 9, "1"), ("Санкт-Петербург", 20, 5, "2"), ("Казань", 12, 4, "3"), ("Новосибирск", 10, 4, "4"), ("Удалённо", 13, 0, "9"),
    ];

    private static List<Person> BuildEmployees(Random rnd)
    {
        var departments = Departments.SelectMany(d => Enumerable.Repeat(d, d.Headcount)).ToArray();
        rnd.Shuffle(departments);

        var usedLogins = new HashSet<string>();
        var people = new List<Person>(departments.Length);
        for (var i = 0; i < departments.Length; i++)
        {
            var dept = departments[i];
            var male = rnd.NextDouble() < 0.55;
            var first = male ? Pick(rnd, MaleNames) : Pick(rnd, FemaleNames);
            var last = rnd.NextDouble() < 0.05 ? Pick(rnd, InvariantSurnames) : Pick(rnd, Surnames) + (male ? "" : "а");
            var contractor = rnd.NextDouble() < 0.08;
            var office = dept.Code is "WH" or "LOG" or "IRB"
                ? WeightedPick(rnd, Offices.Where(o => o.Name != "Удалённо").ToArray(), o => o.Weight)
                : WeightedPick(rnd, Offices, o => o.Weight);
            if (dept.Code == "IRB") office = Offices[0];
            var floor = office.MaxFloor == 0 ? 0 : rnd.NextInt64(1, office.MaxFloor + 1);
            if (dept.Code == "IRB") floor = 7;
            var hireDaysAgo = rnd.Next(40, 12 * 365);
            var score = Math.Round(1 + rnd.NextDouble() * 2 + rnd.NextDouble() * 2, 2);
            var title = Pick(rnd, dept.Titles);
            var profile = BuildProfile(rnd, dept);

            people.Add(MakePerson(i, first, last, dept, title, office.Name, office.Code, floor, hireDaysAgo, contractor, score, profile, usedLogins));
        }

        // Story characters replace whoever got their ids.
        var irb = Departments.Single(d => d.Code == "IRB");
        var it = Departments.Single(d => d.Code == "IT");
        usedLogins.Remove(people[4216].Login);
        usedLogins.Remove(people[3980].Login);
        people[4216] = MakePerson(4216, "Глеб", "Штерн", irb, "Ведущий инженер", "Москва", "1", 7, 6 * 365 + 41, false, 4.62,
            new JsonObject { ["clearance"] = "L3", ["skills"] = new JsonArray("CAD", "оптика", "испытания"), ["languages"] = new JsonArray("ru", "en", "de") }, usedLogins);
        people[3980] = MakePerson(3980, "Кирилл", "Лосев", it, "Системный администратор", "Москва", "1", 3, 214, true, 3.08,
            new JsonObject { ["clearance"] = "L1", ["skills"] = new JsonArray("Linux", "VPN", "сети"), ["languages"] = new JsonArray("ru", "en") }, usedLogins);
        return people;
    }

    private static Person MakePerson(int i, string first, string last, DepartmentInfo dept, string title, string office, string officeCode,
        long floor, int hireDaysAgo, bool contractor, double score, JsonObject profile, HashSet<string> usedLogins)
    {
        var baseLogin = $"{Translit(first)}.{Translit(last)}";
        var login = baseLogin;
        for (var n = 2; !usedLogins.Add(login); n++) login = baseLogin + n;
        var domain = contractor ? "ext.mayak.corp" : "mayak.corp";
        return new Person(
            Index: i,
            Id: $"E{i + 1:D5}",
            First: first,
            Last: last,
            Department: dept.Name,
            Title: title,
            Email: $"{login}@{domain}",
            Login: login,
            Office: office,
            Floor: floor,
            HireDaysAgo: hireDaysAgo,
            IsContractor: contractor,
            Score: score,
            Profile: profile,
            Workstation: $"WS-{dept.Code}-{i + 1:D4}",
            InternalIp: $"10.{officeCode}{floor}.{i / 250 % 250 + 1}.{i % 250 + 1}",
            HomeIp: $"192.0.2.{i % 250 + 1}");
    }

    private static JsonObject BuildProfile(Random rnd, DepartmentInfo dept)
    {
        var skills = dept.Skills.OrderBy(_ => rnd.Next()).Take(rnd.Next(1, Math.Min(4, dept.Skills.Length) + 1)).ToArray();
        var languages = new List<string> { "ru" };
        if (rnd.NextDouble() < 0.45) languages.Add("en");
        if (rnd.NextDouble() < 0.06) languages.Add(Pick(rnd, ["de", "zh", "tr", "kk"]));
        var clearance = dept.Code switch
        {
            "IRB" => "L3",
            "SEC" or "IT" => rnd.NextDouble() < 0.5 ? "L2" : "L1",
            _ => rnd.NextDouble() < 0.1 ? "L2" : "L1",
        };
        return new JsonObject
        {
            ["clearance"] = clearance,
            ["skills"] = new JsonArray(skills.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()),
            ["languages"] = new JsonArray(languages.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()),
        };
    }

    private static TableData EmployeesTable(Context ctx) => new(
        "Employees",
        "Справочник сотрудников «Стеклянного Маяка».",
        [
            new("EmployeeId", KqlType.String, "Табельный номер, E00001…E05000"),
            new("Name", KqlType.String, "Имя и фамилия"),
            new("Department", KqlType.String, "Отдел"),
            new("Title", KqlType.String, "Должность"),
            new("Email", KqlType.String, "Рабочая почта (подрядчики — @ext.mayak.corp)"),
            new("Office", KqlType.String, "Офис или «Удалённо»"),
            new("Floor", KqlType.Long, "Этаж рабочего места (0 — удалённо)"),
            new("HireDate", KqlType.DateTime, "Дата найма"),
            new("IsContractor", KqlType.Bool, "Внешний подрядчик"),
            new("PerformanceScore", KqlType.Real, "Оценка эффективности, 1–5"),
            new("Profile", KqlType.Dynamic, "Профиль: clearance, skills[], languages[]"),
        ],
        ctx.People.Select(p => new object?[]
        {
            p.Id, p.Name, p.Department, p.Title, p.Email, p.Office, p.Floor, ctx.Today.AddDays(-p.HireDaysAgo), p.IsContractor, p.Score, p.Profile,
        }).ToList());

    // ───────────────────────────── network ─────────────────────────────

    private sealed record Service(string Host, string Ip, long Port, string Protocol, string[] Paths, int Weight, string? OnlyDepartment = null);

    private static readonly Service[] Services =
    [
        new("portal.mayak.corp", "10.0.1.10", 443, "HTTPS", ["/", "/news", "/hr/vacation", "/docs/policy", "/phonebook"], 18),
        new("mail.mayak.corp", "10.0.1.20", 443, "HTTPS", ["/owa/", "/owa/inbox", "/owa/calendar"], 22),
        new("wiki.mayak.corp", "10.0.1.30", 443, "HTTPS", ["/spaces/LOG", "/spaces/DEV", "/spaces/HR", "/search"], 8),
        new("helpdesk.mayak.corp", "10.0.1.40", 443, "HTTPS", ["/tickets", "/tickets/new", "/kb/vpn", "/kb/printer"], 4),
        new("git.mayak.corp", "10.0.2.10", 22, "SSH", ["/platform.git", "/tms.git", "/site.git"], 6, "Разработка"),
        new("crm.mayak.corp", "10.0.3.10", 443, "HTTPS", ["/deals", "/clients", "/reports"], 6, "Продажи"),
        new("tms.mayak.corp", "10.0.3.20", 443, "HTTPS", ["/routes", "/shipments", "/drivers"], 10, "Логистика"),
        new("fs-irbis.mayak.corp", "10.0.5.20", 445, "SMB", ["/irbis/specs", "/irbis/tests", "/irbis/reports"], 9, "НИОКР Ирбис"),
        new("search.example", "203.0.113.10", 443, "HTTPS", ["/?q=погода", "/?q=курс+валют", "/?q=kql+summarize", "/?q=пробки"], 9),
        new("news.example", "203.0.113.20", 443, "HTTPS", ["/", "/economy", "/sport"], 5),
        new("video.example", "203.0.113.30", 443, "HTTPS", ["/watch", "/live"], 3),
        new("cloud-docs.example", "203.0.113.40", 443, "HTTPS", ["/d/shared", "/d/new"], 4),
        new("maps.example", "203.0.113.50", 443, "HTTPS", ["/route", "/traffic"], 4),
        new("updates.example", "203.0.113.60", 80, "HTTP", ["/win/patch", "/av/defs"], 3),
    ];

    private static readonly int[] HourWeights = [1, 1, 1, 1, 1, 2, 4, 8, 14, 16, 16, 15, 12, 15, 16, 15, 14, 12, 8, 6, 4, 3, 2, 1];

    private static TableData NetworkTable(Context ctx, Phish phish, Random rnd)
    {
        const int total = 50_000;
        var rows = new List<object?[]>(total);
        var vpnService = new Service("vpn.mayak.corp", "10.0.0.5", 443, "HTTPS", ["/auth"], 0);

        // Phishing page visits (D-6): the victim also submits the form.
        foreach (var (person, n) in phish.Clickers.Select((p, n) => (p, n)))
        {
            var at = ctx.At(Story.PhishDaysAgo, 10, 9 + n * 17, 11 + n * 7);
            rows.Add(NetRow(at, person, false, Story.AttackerIp, 80, "HTTP", $"http://mayak-helpdesk.net/reset?user={person.Login}", "allowed", 640, 18_240, 1.8));
            if (person.Id == Story.VictimId)
                rows.Add(NetRow(at.AddMinutes(2).AddSeconds(14), person, false, Story.AttackerIp, 80, "HTTP", $"http://mayak-helpdesk.net/reset/submit?user={person.Login}", "allowed", 1_830, 2_100, 0.6));
        }

        // The leak (D-2 night): VPN login with stolen credentials, reading fs-irbis, uploading to the drop server.
        var victim = ctx.Victim;
        rows.Add(AttackRow(ctx.At(Story.LeakDaysAgo, 2, 7, 12), victim, "10.0.0.5", 443, "HTTPS", "https://vpn.mayak.corp/auth", 4_210, 12_800, 3.2));
        long[] reads = [301_455_872, 288_112_640, 315_902_976, 297_533_440, 276_824_064, 322_961_408, 309_329_920, 254_803_968];
        for (var i = 0; i < reads.Length; i++)
            rows.Add(AttackRow(ctx.At(Story.LeakDaysAgo, 2, 11 + i * 2, 40 + i * 3), victim, "10.0.5.20", 445, "SMB",
                $"smb://fs-irbis.mayak.corp/irbis/specs/irbis_specs_part{i + 1}.7z", 18_400 + i * 311, reads[i], 52.4 + i * 3.7));
        long[] uploads = [412_090_368, 398_458_880, 405_209_088, 377_913_344, 421_302_272, 301_727_744];
        for (var i = 0; i < uploads.Length; i++)
            rows.Add(AttackRow(ctx.At(Story.LeakDaysAgo, 2, 31 + i * 4, 5 + i * 9), victim, Story.DropIp, 443, "HTTPS",
                $"https://files.dropzone.example/upload/{7_340_112 + i}", uploads[i], 9_800 + i * 120, 188.6 + i * 11.3));

        // Background traffic.
        var active = ctx.People.Where(p => p.Department is not ("Склад" or "Логистика") || rnd.NextDouble() < 0.25).ToArray();
        while (rows.Count < total)
        {
            var person = rnd.NextDouble() < 0.8 ? Pick(rnd, active) : Pick(rnd, ctx.People);
            var day = rnd.Next(1, Days + 1);
            var at = ctx.Today.AddDays(-day).AddHours(WeightedIndex(rnd, HourWeights)).AddSeconds(rnd.Next(3600));
            var vpn = person.Remote || rnd.NextDouble() < 0.07;
            var candidates = Services.Where(s => s.OnlyDepartment == null || s.OnlyDepartment == person.Department).ToArray();
            var service = vpn && rnd.NextDouble() < 0.08 ? vpnService : WeightedPick(rnd, candidates, s => s.Weight);
            var path = Pick(rnd, service.Paths);
            var url = service.Protocol switch
            {
                "SSH" => $"ssh://{service.Host}{path}",
                "SMB" => $"smb://{service.Host}{path}/{Pick(rnd, ["", "draft_", "v2_"])}{rnd.Next(100, 999)}.docx",
                "HTTP" => $"http://{service.Host}{path}",
                _ => $"https://{service.Host}{path}",
            };
            var blocked = rnd.NextDouble() < (service.Host == "video.example" ? 0.15 : 0.015);
            var sent = (long)Math.Min(80_000_000, Math.Exp(Normal(rnd, 8.3, 1.4)));
            var received = (long)Math.Min(400_000_000, Math.Exp(Normal(rnd, service.Protocol == "SMB" ? 13.5 : 10.2, 1.6)));
            if (blocked) { sent = Math.Min(sent, 1_500); received = 0; }
            var duration = Math.Round(Math.Exp(Normal(rnd, 1.4, 1.1)), 2);
            rows.Add(NetRow(at, person, vpn, service.Ip, service.Port, service.Protocol, url, blocked ? "blocked" : "allowed", sent, received, duration));
        }

        return new TableData(
            "NetworkLogs",
            "Журнал прокси и межсетевого экрана: одно соединение — одна строка.",
            [
                new("Timestamp", KqlType.DateTime, "Начало соединения, UTC"),
                new("UserId", KqlType.String, "EmployeeId пользователя"),
                new("Host", KqlType.String, "Рабочая станция или VPN-GW для удалённых подключений"),
                new("SrcIp", KqlType.String, "Адрес источника (для VPN — внешний адрес клиента)"),
                new("DstIp", KqlType.String, "Адрес назначения"),
                new("DstPort", KqlType.Long, "Порт назначения"),
                new("Protocol", KqlType.String, "HTTPS, HTTP, SSH, SMB"),
                new("Url", KqlType.String, "Запрошенный адрес"),
                new("Action", KqlType.String, "allowed / blocked"),
                new("BytesSent", KqlType.Long, "Отправлено байт"),
                new("BytesReceived", KqlType.Long, "Получено байт"),
                new("DurationSec", KqlType.Real, "Длительность соединения, секунды"),
            ],
            rows.OrderBy(r => (DateTime)r[0]!).ToList());

        object?[] NetRow(DateTime at, Person p, bool vpn, string dstIp, long port, string protocol, string url, string action, long sent, long received, double duration) =>
            [at, p.Id, vpn ? "VPN-GW" : p.Workstation, vpn ? p.HomeIp : p.InternalIp, dstIp, port, protocol, url, action, sent, received, duration];

        object?[] AttackRow(DateTime at, Person p, string dstIp, long port, string protocol, string url, long sent, long received, double duration) =>
            [at, p.Id, "VPN-GW", Story.AttackerIp, dstIp, port, protocol, url, "allowed", sent, received, duration];
    }

    // ───────────────────────────── email ─────────────────────────────

    private static Phish PickPhishTargets(Context ctx, Random rnd)
    {
        var moscow = ctx.People.Where(p => p.Office == "Москва" && p.Id != Story.VictimId && p.Id != Story.InsiderId && !p.IsContractor).ToArray();
        var irbis = moscow.Where(p => p.Department == "НИОКР Ирбис").OrderBy(_ => rnd.Next()).Take(9);
        var others = moscow.Where(p => p.Department != "НИОКР Ирбис").OrderBy(_ => rnd.Next()).Take(Story.PhishRecipients - 10);
        var recipients = irbis.Concat(others).Append(ctx.Victim).OrderBy(p => p.Id).ToList();
        var clickers = recipients.Where(p => p.Id != Story.VictimId).OrderBy(_ => rnd.Next()).Take(3).Append(ctx.Victim).OrderBy(p => p.Id).ToList();
        return new Phish(recipients, clickers);
    }

    private static readonly string[] PartnerDomains = ["logistic-partner.example", "bank.example", "supplier.example", "client-corp.example", "port-terminal.example"];
    private static readonly string[] PartnerLocals = ["info", "manager", "sales", "dispatch", "a.petrova", "s.orlov", "office", "buh"];
    private static readonly string[] Mailers = ["Mayak Mail 5.2", "Outlook 16.0", "Thunderbird 115", "Mail.App 17"];

    private static readonly string[] InternalSubjects =
    [
        "Отчёт по отгрузкам", "Встреча по маршрутам", "Re: график смен", "Согласование договора", "Обновление регламента",
        "Заявка на отпуск", "Итоги недели", "Re: инвентаризация склада", "Планёрка в 10:00", "Доступ к TMS", "Re: счёт поставщика",
    ];

    private static readonly string[] InboundSubjects =
    [
        "Счёт на оплату", "Акт сверки", "Заявка на перевозку", "Подтверждение платежа", "Коммерческое предложение",
        "Re: сроки поставки", "Претензия по доставке", "Новые тарифы", "Договор на подпись",
    ];

    private static TableData EmailTable(Context ctx, Phish phish, Random rnd)
    {
        const int total = 20_000;
        var rows = new List<object?[]>(total);
        var tx = 0;

        // Test run of the phishing kit: the only early recipient is the insider (D-8).
        rows.Add(MailRow(ctx.At(8, 16, 42, 9), Story.PhishSender, Story.InsiderEmail, "Inbound", "test",
            $"http://mayak-helpdesk.net/reset?user={ctx.Insider.Login}", null, PhishHeaders(), 3.1, 4_388));

        // An earlier lookalike: fake billing notices to finance (D-14).
        foreach (var p in ctx.People.Where(p => p.Department == "Финансы" && p.Office == "Москва").Take(3))
            rows.Add(MailRow(ctx.At(14, 9, 12 + p.Index % 40, p.Index % 60), "billing@mayak-pay.net", p.Email, "Inbound", "Неоплаченный счёт за хостинг",
                "http://mayak-pay.net/invoice", null, Headers("<bounce@mayak-pay.net>", Story.AttackerIp, "fail", "PHPMailer 6.8"), 6.4, 5_102));

        // The phishing campaign itself (D-6, 10:00–10:40).
        foreach (var (p, n) in phish.Recipients.Select((p, n) => (p, n)))
            rows.Add(MailRow(ctx.At(Story.PhishDaysAgo, 10, n * 40 / phish.Recipients.Count, (n * 37) % 60), Story.PhishSender, p.Email, "Inbound",
                Story.PhishSubject, $"http://mayak-helpdesk.net/reset?user={p.Login}", null, PhishHeaders(), Math.Round(4.0 + n % 10 * 0.2, 1), 4_391 + n * 3));

        // The insider forwards access lists to a private mailbox (D-3 evening).
        rows.Add(MailRow(ctx.At(3, 22, 17, 40), Story.InsiderEmail, "k.losev.box@mailbox.example", "Outbound", "fwd: доступы", "",
            Attachments(("irbis_access_matrix.xlsx", 48_211), ("vpn_howto.pdf", 311_402)),
            Headers($"<{Story.InsiderEmail}>", ctx.Insider.InternalIp, "pass", "Mayak Mail 5.2"), 0.3, 372_904));

        // Planned password change announcement (D-20): a legitimate "пароль" email.
        foreach (var p in ctx.People.Where(p => p.Index % 41 == 0))
            rows.Add(MailRow(ctx.At(20, 8, 30, p.Index % 50), "it@mayak.corp", p.Email, "Internal", "Плановая смена: обновите пароль домена до пятницы", "https://helpdesk.mayak.corp/kb/password",
                null, Headers("<it@mayak.corp>", "10.0.1.20", "pass", "Mayak Mail 5.2"), 0.1, 6_120));

        var irbis = ctx.People.Where(p => p.Department == "НИОКР Ирбис").ToArray();
        while (rows.Count < total)
        {
            var at = ctx.Today.AddDays(-rnd.Next(1, Days + 1)).AddHours(WeightedIndex(rnd, HourWeights)).AddSeconds(rnd.Next(3600));
            var roll = rnd.NextDouble();
            var me = Pick(rnd, ctx.People);
            string sender, recipient, direction, subject, ip;
            if (roll < 0.55)
            {
                direction = "Internal";
                sender = me.Email; ip = me.InternalIp;
                if (me.Department == "НИОКР Ирбис" && rnd.NextDouble() < 0.5)
                {
                    recipient = Pick(rnd, irbis).Email;
                    subject = Pick(rnd, ["Ирбис: результаты испытаний", "Ирбис: ревизия КД", "Ирбис: протокол совещания"]);
                }
                else
                {
                    recipient = Pick(rnd, ctx.People).Email;
                    subject = Pick(rnd, InternalSubjects);
                }
            }
            else if (roll < 0.85)
            {
                direction = "Inbound";
                sender = $"{Pick(rnd, PartnerLocals)}@{Pick(rnd, PartnerDomains)}"; recipient = me.Email;
                subject = Pick(rnd, InboundSubjects); ip = $"203.0.113.{rnd.Next(100, 200)}";
            }
            else
            {
                direction = "Outbound";
                sender = me.Email; recipient = $"{Pick(rnd, PartnerLocals)}@{Pick(rnd, PartnerDomains)}";
                subject = "Re: " + Pick(rnd, InboundSubjects); ip = me.InternalIp;
            }

            if (rnd.NextDouble() < 0.12) subject += $" №{rnd.Next(1000, 9999)}";
            JsonArray? attachments = null;
            if (rnd.NextDouble() < 0.35)
            {
                var count = rnd.Next(1, 4);
                var files = new (string, long)[count];
                for (var i = 0; i < count; i++) files[i] = (AttachmentName(rnd, subject), rnd.Next(12_000, 4_000_000));
                attachments = Attachments(files);
            }

            var spf = direction == "Inbound" && rnd.NextDouble() < 0.04 ? "softfail" : "pass";
            var spam = direction == "Inbound" ? Math.Round(0.2 + rnd.NextDouble() * 3.3, 1) : Math.Round(rnd.NextDouble(), 1);
            var size = rnd.Next(2_000, 50_000) + (attachments?.Sum(a => (long)a!["size"]!) ?? 0);
            var url = direction == "Inbound" && rnd.NextDouble() < 0.15 ? $"https://{sender.Split('@')[1]}/doc/{rnd.Next(10_000, 99_999)}" : "";
            rows.Add(MailRow(at, sender, recipient, direction, subject, url, attachments, Headers($"<{sender}>", ip, spf, Pick(rnd, Mailers)), spam, size));
        }

        return new TableData(
            "EmailEvents",
            "Почтовый шлюз: каждое письмо каждому получателю.",
            [
                new("Timestamp", KqlType.DateTime, "Время доставки, UTC"),
                new("MessageId", KqlType.String, "Идентификатор письма"),
                new("Sender", KqlType.String, "Отправитель"),
                new("Recipient", KqlType.String, "Получатель"),
                new("Direction", KqlType.String, "Inbound / Outbound / Internal"),
                new("Subject", KqlType.String, "Тема"),
                new("Url", KqlType.String, "Первая ссылка в теле письма или пустая строка"),
                new("HasAttachment", KqlType.Bool, "Есть ли вложения"),
                new("Attachments", KqlType.Dynamic, "Массив вложений: [{name, size, sha256}]"),
                new("Headers", KqlType.String, "Заголовки письма — JSON-строка (нужен parse_json)"),
                new("SpamScore", KqlType.Real, "Оценка антиспама, 0–10"),
                new("SizeBytes", KqlType.Long, "Размер письма с вложениями"),
            ],
            rows.OrderBy(r => (DateTime)r[0]!).ToList());

        object?[] MailRow(DateTime at, string sender, string recipient, string direction, string subject, string url, JsonArray? attachments, string headers, double spam, long size)
        {
            var id = $"<{Hex($"{Seed}/{tx++}", 12)}@{sender.Split('@')[1]}>";
            return [at, id, sender, recipient, direction, subject, url, attachments is { Count: > 0 }, attachments ?? new JsonArray(), headers, spam, size];
        }

        string PhishHeaders() => Headers("<bounce@mayak-helpdesk.net>", Story.AttackerIp, "fail", "PHPMailer 6.8");
    }

    private static string Headers(string returnPath, string ip, string spf, string mailer) =>
        new JsonObject { ["Return-Path"] = returnPath, ["X-Originating-IP"] = ip, ["SPF"] = spf, ["X-Mailer"] = mailer }.ToJsonString();

    private static JsonArray Attachments(params (string Name, long Size)[] files) =>
        new(files.Select(f => (JsonNode?)new JsonObject { ["name"] = f.Name, ["size"] = f.Size, ["sha256"] = Hex(f.Name + f.Size, 64) }).ToArray());

    private static string AttachmentName(Random rnd, string subject)
    {
        var n = rnd.Next(100, 9999);
        if (subject.StartsWith("Ирбис")) return Pick(rnd, [$"irbis_test_{n}.pdf", $"irbis_kd_rev{n % 9 + 1}.dwg", $"irbis_protocol_{n}.docx"]);
        return Pick(rnd, [$"invoice_{n}.pdf", $"akt_{n}.pdf", $"route_{n}.xlsx", $"photo_{n}.jpg", $"report_{n}.docx", $"contract_{n}.docx", $"price_{n}.xlsx", $"scan_{n}.zip"]);
    }

    // ───────────────────────────── badges ─────────────────────────────

    private static readonly Dictionary<string, string> Buildings = new()
    {
        ["Москва"] = "Маяк-Москва", ["Санкт-Петербург"] = "Маяк-Петербург", ["Казань"] = "Маяк-Казань", ["Новосибирск"] = "Маяк-Новосибирск",
    };

    private static TableData BadgeTable(Context ctx, Random rnd)
    {
        var rows = new List<object?[]>(32_000);
        var onsite = ctx.People.Where(p => !p.Remote).ToArray();
        // Moscow HQ staff come regularly; everyone else shows up occasionally.
        var presence = onsite.ToDictionary(p => p.Id, p =>
            p.Department is "НИОКР Ирбис" or "ИТ" or "Безопасность" && p.Office == "Москва" ? 0.7 :
            p.Office == "Москва" && p.Index % 5 == 0 ? 0.2 : 0.012);

        for (var day = Days; day >= 1; day--)
        {
            foreach (var p in onsite)
            {
                if (rnd.NextDouble() >= presence[p.Id]) continue;
                if (p.Id == Story.VictimId && day is 3 or 2) continue; // written explicitly below
                var arrive = ctx.Today.AddDays(-day).AddHours(7.5 + rnd.NextDouble() * 3);
                var leave = arrive.AddHours(8 + rnd.NextDouble() * 2.5);
                rows.AddRange(Workday(rnd, p, arrive, leave));
            }
        }

        // The victim leaves at 19:04 on D-3 and is back only at 09:12 on D-2: nobody was at his desk that night.
        var v = ctx.Victim;
        rows.AddRange(Workday(rnd, v, ctx.At(3, 8, 51, 2), ctx.At(3, 19, 4, 31)));
        rows.AddRange(Workday(rnd, v, ctx.At(2, 9, 12, 44), ctx.At(2, 18, 37, 5)));

        // The insider tries the Irbis lab door in the evenings (D-9…D-4).
        foreach (var (day, minute) in new[] { (9, 31), (8, 47), (7, 12), (5, 55), (4, 20) })
            rows.Add(BadgeRow(ctx.At(day, 19, minute, day * 7 % 60), ctx.Insider, "Маяк-Москва", "Лаборатория Ирбис", 7, "in", "denied"));

        // A few honest mistakes at the lab door by other Moscow employees.
        foreach (var (p, n) in ctx.People.Where(p => p.Office == "Москва" && p.Department is "Разработка" or "Маркетинг").Take(6).Select((p, n) => (p, n)))
            rows.Add(BadgeRow(ctx.At(1 + n * 4, 11 + n, 5 * n, 13), p, "Маяк-Москва", "Лаборатория Ирбис", 7, "in", "denied"));

        // Late evenings in the lab: some Irbis engineers stay past midnight.
        foreach (var (p, n) in ctx.People.Where(p => p.Department == "НИОКР Ирбис" && p.Id != Story.VictimId).Take(4).Select((p, n) => (p, n)))
        {
            rows.Add(BadgeRow(ctx.At(5 + n * 3, 0, 20 + n * 7, 5), p, "Маяк-Москва", "Лаборатория Ирбис", 7, "out", "granted"));
            rows.Add(BadgeRow(ctx.At(5 + n * 3, 0, 31 + n * 7, 41), p, "Маяк-Москва", "Главный вход", 1, "out", "granted"));
        }

        return new TableData(
            "BadgeEvents",
            "Система контроля доступа: проходы по пропускам.",
            [
                new("Timestamp", KqlType.DateTime, "Время прохода, UTC"),
                new("EmployeeId", KqlType.String, "Владелец пропуска"),
                new("BadgeId", KqlType.String, "Номер пропуска"),
                new("Building", KqlType.String, "Здание"),
                new("Door", KqlType.String, "Дверь или турникет"),
                new("Floor", KqlType.Long, "Этаж двери"),
                new("Direction", KqlType.String, "in / out"),
                new("Result", KqlType.String, "granted / denied"),
            ],
            rows.OrderBy(r => (DateTime)r[0]!).ToList());

        IEnumerable<object?[]> Workday(Random r, Person p, DateTime arrive, DateTime leave)
        {
            var building = Buildings[p.Office];
            yield return BadgeRow(arrive, p, building, "Главный вход", 1, "in", "granted");
            yield return BadgeRow(arrive.AddMinutes(1 + r.Next(3)), p, building, $"Этаж {p.Floor}", p.Floor, "in", "granted");
            if (p.Department == "НИОКР Ирбис")
                yield return BadgeRow(arrive.AddMinutes(4 + r.Next(10)), p, building, "Лаборатория Ирбис", 7, "in", "granted");
            if (p.Department == "ИТ" && r.NextDouble() < 0.3)
                yield return BadgeRow(arrive.AddHours(1 + r.NextDouble() * 5), p, building, "Серверная", 1, "in", "granted");
            if (p.Department is "Склад" or "Логистика" && r.NextDouble() < 0.6)
                yield return BadgeRow(arrive.AddMinutes(20 + r.Next(60)), p, building, "Склад", 1, "in", "granted");
            if (r.NextDouble() < 0.45)
                yield return BadgeRow(arrive.Date.AddHours(12 + r.NextDouble() * 2), p, building, "Столовая", 2, "in", "granted");
            if (r.NextDouble() < 0.012)
                yield return BadgeRow(arrive.AddHours(2 + r.NextDouble() * 4), p, building, Pick(r, ["Серверная", "Склад", "Парковка"]), 1, "in", "denied");
            yield return BadgeRow(leave, p, building, r.NextDouble() < 0.2 ? "Парковка" : "Главный вход", 1, "out", "granted");
        }

        static object?[] BadgeRow(DateTime at, Person p, string building, string door, long floor, string direction, string result) =>
            [at, p.Id, $"B{p.Index * 7919 % 90_000 + 10_000}", building, door, floor, direction, result];
    }

    // ───────────────────────────── transactions ─────────────────────────────

    private sealed record Merchant(string Name, string Category, int Mcc, double Min, double Max, string Currency, string Channel, int Weight, string[] Tags);

    private static readonly Merchant[] Merchants =
    [
        new("ГородТакси", "Такси", 4121, 250, 2500, "RUB", "online", 14, ["travel", "client", "night"]),
        new("ТаксиГо", "Такси", 4121, 220, 2200, "RUB", "online", 10, ["travel", "night"]),
        new("Кофейня «Маяк»", "Кафе и рестораны", 5812, 180, 900, "RUB", "pos", 12, ["team"]),
        new("Ресторан «Север»", "Кафе и рестораны", 5812, 1800, 14000, "RUB", "pos", 5, ["client", "team"]),
        new("Пекарня №5", "Кафе и рестораны", 5812, 150, 700, "RUB", "pos", 6, []),
        new("АэроТревел", "Авиабилеты", 4511, 4500, 38000, "RUB", "online", 5, ["travel"]),
        new("Небесный Путь", "Авиабилеты", 4511, 5200, 42000, "RUB", "online", 3, ["travel", "urgent"]),
        new("Отель «Причал»", "Отели", 7011, 3500, 18000, "RUB", "pos", 4, ["travel"]),
        new("Гостиница «Транзит»", "Отели", 7011, 2800, 9000, "RUB", "pos", 3, ["travel"]),
        new("Офисмаг-Плюс", "Канцелярия", 5943, 150, 9000, "RUB", "online", 6, []),
        new("АЗС «Дорога»", "Топливо", 5541, 1500, 7000, "RUB", "pos", 9, ["fleet"]),
        new("АЗС «Северная»", "Топливо", 5541, 1500, 6500, "RUB", "pos", 6, ["fleet"]),
        new("МобиЛайн", "Связь", 4814, 300, 1500, "RUB", "online", 4, []),
        new("DevTools Pro", "ПО и подписки", 5734, 19, 99, "USD", "online", 2, ["infra"]),
        new("Office Suite Online", "ПО и подписки", 5734, 12, 30, "USD", "online", 2, []),
        new("MapData API", "ПО и подписки", 5734, 50, 300, "USD", "online", 1, ["infra"]),
    ];

    private static TableData TransactionsTable(Context ctx, Random rnd)
    {
        const int total = 15_000;
        var rows = new List<object?[]>(total);
        var holders = ctx.People.Where(p => p.Department is "Продажи" or "Логистика" or "ИТ" or "Разработка" or "Маркетинг" or "НИОКР Ирбис" && p.Index % 3 == 0).ToArray();
        var it = ctx.People.Where(p => p.Department == "ИТ" && p.Id != Story.InsiderId).ToArray();
        var tx = 0;

        // Legitimate VPS rentals by IT for build agents.
        for (var i = 0; i < 12; i++)
        {
            var p = it[i * 7 % it.Length];
            rows.Add(TxRow(ctx.Today.AddDays(-(2 + i * 2)).AddHours(10 + i % 6).AddMinutes(i * 4), p, "CloudNest VPS", "ПО и подписки", Math.Round(9.5 + i * 1.5, 2), "EUR", true,
                Details(4816, "online", "NL", "Amsterdam", ["infra"], new JsonObject { ["service"] = "vps", ["plan"] = "S-1", ["ip"] = $"192.0.2.{200 + i}", ["hostname"] = $"build-agent-{i + 1}" })));
        }

        // The insider buys the lookalike domain (D-10) and the server used for phishing and the VPN login (D-9).
        var ins = ctx.Insider;
        rows.Add(TxRow(ctx.At(10, 21, 3, 17), ins, "NameHost Domains", "ПО и подписки", 12.99, "USD", true,
            Details(4816, "online", "US", "Phoenix", ["infra"], new JsonObject { ["service"] = "domain", ["domain"] = "mayak-helpdesk.net", ["years"] = 1 })));
        rows.Add(TxRow(ctx.At(9, 13, 22, 48), ins, "CloudNest VPS", "ПО и подписки", 38.0, "EUR", true,
            Details(4816, "online", "NL", "Amsterdam", ["infra", "urgent"], new JsonObject { ["service"] = "vps", ["plan"] = "M-4", ["ip"] = Story.AttackerIp, ["hostname"] = "helpdesk-prod" })));

        while (rows.Count < total)
        {
            var p = Pick(rnd, holders);
            var m = WeightedPick(rnd, Merchants, x => x.Weight);
            var at = ctx.Today.AddDays(-rnd.Next(1, Days + 1)).AddHours(WeightedIndex(rnd, HourWeights)).AddSeconds(rnd.Next(3600));
            var amount = Math.Round(m.Min + (m.Max - m.Min) * Math.Pow(rnd.NextDouble(), 1.8), m.Currency == "RUB" ? 0 : 2);
            var approved = rnd.NextDouble() >= 0.025;
            var tags = m.Tags.Where(_ => rnd.NextDouble() < 0.35).ToArray();
            var city = m.Category is "Авиабилеты" or "Отели" ? Pick(rnd, ["Москва", "Санкт-Петербург", "Казань", "Новосибирск", "Екатеринбург", "Владивосток"]) : p.Office == "Удалённо" ? "Москва" : p.Office;
            var details = Details(m.Mcc, m.Channel, m.Currency == "RUB" ? "RU" : "US", m.Currency == "RUB" ? city : "Online", tags);
            if (!approved) details["reason"] = Pick(rnd, ["limit_exceeded", "suspected_fraud", "insufficient_funds"]);
            rows.Add(TxRow(at, p, m.Name, m.Category, amount, m.Currency, approved, details));
        }

        return new TableData(
            "Transactions",
            "Операции по корпоративным картам.",
            [
                new("Timestamp", KqlType.DateTime, "Время операции, UTC"),
                new("TxId", KqlType.String, "Идентификатор операции"),
                new("EmployeeId", KqlType.String, "Держатель карты"),
                new("Merchant", KqlType.String, "Продавец"),
                new("Category", KqlType.String, "Категория расходов"),
                new("Amount", KqlType.Real, "Сумма в валюте операции"),
                new("Currency", KqlType.String, "RUB, USD, EUR"),
                new("Approved", KqlType.Bool, "Одобрена ли операция"),
                new("Details", KqlType.Dynamic, "Детали: mcc, channel, geo{country, city}, tags[], invoice{…}, reason"),
            ],
            rows.OrderBy(r => (DateTime)r[0]!).ToList());

        object?[] TxRow(DateTime at, Person p, string merchant, string category, double amount, string currency, bool approved, JsonObject details) =>
            [at, $"TX-{1_000_000 + tx++ * 37 % 9_000_000}", p.Id, merchant, category, amount, currency, approved, details];

        static JsonObject Details(int mcc, string channel, string country, string city, string[] tags, JsonObject? invoice = null)
        {
            var d = new JsonObject
            {
                ["mcc"] = mcc,
                ["channel"] = channel,
                ["geo"] = new JsonObject { ["country"] = country, ["city"] = city },
                ["tags"] = new JsonArray(tags.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray()),
            };
            if (invoice != null) d["invoice"] = invoice;
            return d;
        }
    }

    // ───────────────────────────── helpers ─────────────────────────────

    private static T Pick<T>(Random rnd, IReadOnlyList<T> items) => items[rnd.Next(items.Count)];

    private static T WeightedPick<T>(Random rnd, IReadOnlyList<T> items, Func<T, int> weight)
    {
        var sum = items.Sum(weight);
        var x = rnd.Next(sum);
        foreach (var item in items)
        {
            x -= weight(item);
            if (x < 0) return item;
        }
        return items[^1];
    }

    private static int WeightedIndex(Random rnd, int[] weights)
    {
        var x = rnd.Next(weights.Sum());
        for (var i = 0; i < weights.Length; i++)
        {
            x -= weights[i];
            if (x < 0) return i;
        }
        return weights.Length - 1;
    }

    private static double Normal(Random rnd, double mean, double sd)
    {
        var u1 = 1.0 - rnd.NextDouble();
        var u2 = rnd.NextDouble();
        return mean + sd * Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }

    private static string Hex(string seed, int length) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed))).ToLowerInvariant()[..length];

    private static readonly Dictionary<char, string> TranslitMap = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "e", ['ж'] = "zh", ['з'] = "z",
        ['и'] = "i", ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n", ['о'] = "o", ['п'] = "p", ['р'] = "r",
        ['с'] = "s", ['т'] = "t", ['у'] = "u", ['ф'] = "f", ['х'] = "kh", ['ц'] = "ts", ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "shch",
        ['ъ'] = "", ['ы'] = "y", ['ь'] = "", ['э'] = "e", ['ю'] = "yu", ['я'] = "ya",
    };

    private static string Translit(string s) =>
        string.Concat(s.ToLower(CultureInfo.GetCultureInfo("ru-RU")).Select(c => TranslitMap.TryGetValue(c, out var t) ? t : c.ToString()));
}
