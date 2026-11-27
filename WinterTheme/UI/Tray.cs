using Avalonia.Controls;

namespace WinterTheme.UI;

/// <summary>Menu bar icon with all the knobs.</summary>
public static class Tray
{
    public static void Create(App app)
    {
        var s = Settings.Current;
        var menu = new NativeMenu();

        // intensity
        var intensity = new NativeMenuItem("Снегопад");
        var sub = new NativeMenu();
        var radios = new List<NativeMenuItem>();
        string[] names = { "Лёгкий", "Обычный", "Метель" };
        for (int i = 0; i < names.Length; i++)
        {
            int k = i;
            var it = new NativeMenuItem(names[i]) { ToggleType = NativeMenuItemToggleType.Radio, IsChecked = s.Intensity == i };
            it.Click += (_, _) =>
            {
                s.Intensity = k;
                foreach (var r in radios) r.IsChecked = r == it;
                s.Save();
            };
            radios.Add(it);
            sub.Items.Add(it);
        }
        intensity.Menu = sub;
        menu.Items.Add(intensity);

        menu.Items.Add(Toggle("Остановить снегопад", () => s.Paused, v => s.Paused = v));
        menu.Items.Add(Toggle("Снег на окнах", () => s.SnowOnWindows, v => { s.SnowOnWindows = v; app.ApplyWindowSettings(); }));
        menu.Items.Add(Toggle("Гирлянда", () => s.Garland, v => s.Garland = v));
        menu.Items.Add(Toggle("Курсор раздвигает сугробы", () => s.CursorPush, v => s.CursorPush = v));
        menu.Items.Add(Toggle("Таяние", () => s.Melt, v => s.Melt = v));
        menu.Items.Add(Toggle("Мониторы как один экран", () => s.LinkScreens, v => s.LinkScreens = v));
        menu.Items.Add(Toggle("Сосульки", () => s.Icicles, v => s.Icicles = v));

        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(Action("Вызвать трактор", () => app.OnActiveWorld(w => w.StartTractor())));
        menu.Items.Add(Action("Выпустить Гринча", () => app.OnActiveWorld(w => w.StartGrinch())));
        menu.Items.Add(Action("Позвать Санту", () => app.OnActiveWorld(w => w.StartSanta())));
        menu.Items.Add(Action("Салют!", () => app.ForEachWorld(w => w.StartFireworks(12))));
        menu.Items.Add(Action("Падающая звезда", () => app.OnActiveWorld(w => w.ShootingStar())));
        menu.Items.Add(Action("Северное сияние", () => app.ForEachWorld(w => w.StartAurora())));
        menu.Items.Add(Action("Позвать снегирей", () => app.OnActiveWorld(w => w.SpawnFlock())));
        menu.Items.Add(Action("Репетиция Нового года", () => app.ForEachWorld(w => w.RehearseNewYear())));
        menu.Items.Add(Action("Убрать весь снег", () => app.ForEachWorld(w => w.ClearAll())));
        menu.Items.Add(Action("Растопить снеговиков", () => app.ForEachWorld(w =>
        {
            foreach (var b in w.Balls) w.Powder(b.X, b.Y, 10, b.R * 2, 80);
            w.Balls.Clear();
        })));

        var auto = new NativeMenuItem("Сами приходят");
        var autoMenu = new NativeMenu();
        autoMenu.Items.Add(Toggle("Трактор (когда много снега)", () => s.AutoTractor, v => s.AutoTractor = v));
        autoMenu.Items.Add(Toggle("Гринч", () => s.RandomGrinch, v => s.RandomGrinch = v));
        autoMenu.Items.Add(Toggle("Санта", () => s.RandomSanta, v => s.RandomSanta = v));
        autoMenu.Items.Add(Toggle("Падающие звёзды", () => s.ShootingStars, v => s.ShootingStars = v));
        autoMenu.Items.Add(Toggle("Снеговики болтают", () => s.Chatter, v => s.Chatter = v));
        autoMenu.Items.Add(Toggle("Снегири", () => s.Birds, v => s.Birds = v));
        autoMenu.Items.Add(Toggle("Северное сияние по ночам", () => s.Aurora, v => s.Aurora = v));
        autoMenu.Items.Add(Toggle("Отсчёт и салют в новогоднюю полночь", () => s.NewYearShow, v => s.NewYearShow = v));
        auto.Menu = autoMenu;
        menu.Items.Add(auto);

        if (LoginItem.Available)
            menu.Items.Add(Toggle("Запускать при входе", () => LoginItem.Enabled, LoginItem.Set));
        menu.Items.Add(Toggle("Плавно (60 fps, больше CPU)", () => s.Smooth60, v => s.Smooth60 = v));
        menu.Items.Add(Toggle("Видно на скриншотах", () => s.ShowInScreenshots, v => { s.ShowInScreenshots = v; app.ApplyWindowSettings(); }));

        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(new NativeMenuItem("Снеговик: зажми ⌥ и катай ком по сугробу") { IsEnabled = false });
        menu.Items.Add(new NativeMenuItem("Брось ком на ком побольше — 3 кома = снеговик") { IsEnabled = false });
        menu.Items.Add(new NativeMenuItem("Клик по подарку — открыть (бывают сюрпризы)") { IsEnabled = false });
        menu.Items.Add(new NativeMenuItem("Швырни маленький ком — снежок. Попади в Гринча!") { IsEnabled = false });
        menu.Items.Add(new NativeMenuItem("Клик по саням Санты — ещё подарок") { IsEnabled = false });
        menu.Items.Add(new NativeMenuItem("Слетела шапка? Кликни по ней") { IsEnabled = false });
        menu.Items.Add(new NativeMenuItem("И ещё есть пасхалки...") { IsEnabled = false });
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(Action("Выход", app.Quit));

        var tray = new TrayIcon
        {
            Icon = IconFactory.Snowflake(),
            ToolTipText = "Зимняя тема",
            Menu = menu,
            IsVisible = true,
        };
        TrayIcon.SetIcons(app, new TrayIcons { tray });
    }

    private static NativeMenuItem Toggle(string header, Func<bool> get, Action<bool> set)
    {
        var it = new NativeMenuItem(header) { ToggleType = NativeMenuItemToggleType.CheckBox, IsChecked = get() };
        it.Click += (_, _) =>
        {
            set(!get());
            it.IsChecked = get();
            Settings.Current.Save();
        };
        return it;
    }

    private static NativeMenuItem Action(string header, Action action)
    {
        var it = new NativeMenuItem(header);
        it.Click += (_, _) => action();
        return it;
    }
}
