using System;
using System.Collections.Generic;

namespace Roomy
{
    enum Verdict
    {
        System,     // ядро Windows — не трогать
        Windows,    // компонент Windows — обычно нужен
        Optional,   // можно отключить, если не пользуетесь
        App,        // обычная программа пользователя
        Caution,    // требует внимания
        Unknown     // нет в базе
    }

    class KbEntry
    {
        public string Name;       // понятное название
        public Verdict Verdict;
        public string What;       // что это
        public string IfClosed;   // что будет, если закрыть/отключить
    }

    // встроенная база частых процессов. Только то, что известно наверняка.
    static class ProcessKb
    {
        static readonly Dictionary<string, KbEntry> Db = new Dictionary<string, KbEntry>(StringComparer.OrdinalIgnoreCase);

        static void Add(string keys, string name, Verdict v, string what, string ifClosed)
        {
            var e = new KbEntry { Name = name, Verdict = v, What = what, IfClosed = ifClosed };
            foreach (var k in keys.Split('|')) Db[Settings.Norm(k)] = e;
        }

        public static KbEntry Find(string key)
        {
            KbEntry e;
            return Db.TryGetValue(Settings.Norm(key), out e) ? e : null;
        }

        // имена системных процессов, которые всегда лежат в папке Windows.
        public static readonly HashSet<string> SystemNames = Settings.Set(
            "svchost", "csrss", "lsass", "winlogon", "wininit", "services", "smss", "dwm", "explorer", "conhost",
            "rundll32", "taskhostw", "runtimebroker", "spoolsv", "sihost", "ctfmon", "dllhost", "fontdrvhost",
            "audiodg", "searchindexer", "wmiprvse", "regsvr32", "mshta", "wscript", "cscript", "lsaiso");

        static readonly string NoTouch = L.T("Закрывать нельзя: Windows перестанет работать правильно или перезагрузится.");

        static ProcessKb()
        {
            // ---------- Ядро Windows ----------
            Add("system", L.T("Ядро Windows (System)"), Verdict.System,
                L.T("Сама операционная система: драйверы, работа с дисками и памятью. Иногда показывает высокую нагрузку из-за драйверов."), NoTouch);
            Add("registry", L.T("Реестр Windows"), Verdict.System,
                L.T("Хранит в памяти реестр — базу настроек Windows и программ."), NoTouch);
            Add("memory compression", L.T("Сжатие памяти"), Verdict.System,
                L.T("Здесь Windows хранит сжатые данные программ, чтобы реже выгружать их на диск. Чем больше он «занимает», тем больше памяти он на самом деле сэкономил."), NoTouch);
            Add("secure system|lsaiso", L.T("Защищённая среда Windows"), Verdict.System,
                L.T("Часть защиты на основе виртуализации: изолированно хранит пароли и проверяет драйверы."), NoTouch);
            Add("smss", L.T("Диспетчер сеансов Windows"), Verdict.System,
                L.T("Запускает Windows и сеансы пользователей при включении компьютера."), NoTouch);
            Add("csrss", L.T("Исполняемая среда клиент-сервер"), Verdict.System,
                L.T("Основной процесс подсистемы Windows: окна, консоль, завершение работы."), NoTouch);
            Add("wininit", L.T("Запуск Windows"), Verdict.System,
                L.T("Запускает службы и системные процессы при старте Windows."), NoTouch);
            Add("winlogon", L.T("Вход в Windows"), Verdict.System,
                L.T("Отвечает за вход и выход из системы, экран блокировки, Ctrl+Alt+Del."), NoTouch);
            Add("services", L.T("Диспетчер служб"), Verdict.System,
                L.T("Запускает и останавливает все службы Windows."), NoTouch);
            Add("lsass", L.T("Проверка подлинности (LSASS)"), Verdict.System,
                L.T("Проверяет пароли при входе, управляет политиками безопасности. Частая цель вирусов, поэтому настоящий lsass всегда лежит в C:\\Windows\\System32."), NoTouch);
            Add("svchost", L.T("Хост-процесс служб Windows"), Verdict.System,
                L.T("Один svchost — это контейнер для одной или нескольких служб Windows (сеть, звук, обновления и т. д.), поэтому их всегда много. Настоящий svchost лежит только в C:\\Windows\\System32."),
                NoTouch + L.T(" Если какой-то svchost сильно грузит систему — смотрите, какая служба внутри него (Диспетчер задач → Подробности → правый клик → «Перейти к службам»)."));
            Add("dwm", L.T("Диспетчер окон рабочего стола"), Verdict.System,
                L.T("Рисует окна, прозрачность и анимации. Без него нет картинки на экране."), L.T("Windows сразу перезапустит его; экран может мигнуть."));
            Add("explorer", L.T("Проводник Windows"), Verdict.System,
                L.T("Рабочий стол, панель задач, меню «Пуск» и окна с папками."), L.T("Пропадут панель задач и рабочий стол, пока Проводник не запустится снова."));
            Add("fontdrvhost", L.T("Драйвер шрифтов"), Verdict.System, L.T("Отображает шрифты в изолированном процессе для безопасности."), NoTouch);
            Add("sihost", L.T("Оболочка Windows (Shell Infrastructure Host)"), Verdict.System,
                L.T("Отвечает за уведомления, фон рабочего стола, часть панели задач."), NoTouch);
            Add("ctfmon", L.T("Ввод текста и раскладки"), Verdict.System,
                L.T("Переключение языка, рукописный и голосовой ввод, сенсорная клавиатура."), L.T("Может перестать работать переключение раскладки."));
            Add("audiodg", L.T("Звуковой движок Windows"), Verdict.System,
                L.T("Обрабатывает весь звук, включая эффекты звуковой карты. Повышенная нагрузка обычно из-за «улучшений» звука от драйвера."), L.T("Пропадёт звук, пока Windows его не перезапустит."));
            Add("conhost", L.T("Окно консоли"), Verdict.Windows,
                L.T("Обслуживает окна командной строки и консольные программы. Их бывает несколько — это нормально."), L.T("Закроется связанная консольная программа."));
            Add("dllhost", L.T("Хост COM-объектов (COM Surrogate)"), Verdict.Windows,
                L.T("Выполняет системные компоненты, например создание эскизов картинок и видео в Проводнике."), L.T("Обычно безвредно, но может прерваться работа Проводника."));
            Add("taskhostw", L.T("Хост задач Windows"), Verdict.Windows,
                L.T("Выполняет фоновые задачи Windows из Планировщика."), L.T("Может прерваться какая-то фоновая задача."));
            Add("runtimebroker", L.T("Посредник разрешений приложений"), Verdict.Windows,
                L.T("Проверяет разрешения приложений из Microsoft Store: камера, микрофон, местоположение."), L.T("Перезапустится при следующем обращении приложения."));
            Add("spoolsv", L.T("Диспетчер печати"), Verdict.Windows,
                L.T("Служба печати. Если принтера нет, её можно отключить в службах, но выигрыш по памяти небольшой."), L.T("Перестанет работать печать."));
            Add("wmiprvse", L.T("Поставщик WMI"), Verdict.Windows,
                L.T("Отдаёт программам сведения о системе (в том числе Roomy при проверке компьютера). Бывает загружен, когда какая-то программа часто опрашивает систему."), L.T("Перезапустится при следующем запросе."));
            Add("wudfhost", L.T("Драйверы пользовательского режима"), Verdict.Windows,
                L.T("Работа некоторых устройств: датчики, сканер отпечатков, USB-устройства."), L.T("Может отключиться какое-то устройство."));
            Add("dashost", L.T("Сопряжение устройств"), Verdict.Windows, L.T("Подключение Bluetooth и сетевых устройств."), L.T("Могут отвалиться беспроводные устройства."));
            Add("backgroundtaskhost", L.T("Фоновые задачи приложений"), Verdict.Windows, L.T("Фоновые задачи приложений из Microsoft Store (обновление плиток, уведомления)."), L.T("Обычно безвредно."));
            Add("searchhost|searchapp", L.T("Поиск Windows (интерфейс)"), Verdict.Windows, L.T("Окно поиска в меню «Пуск»."), L.T("Перезапустится при следующем открытии поиска."));
            Add("searchindexer", L.T("Индексатор поиска"), Verdict.Windows,
                L.T("Составляет каталог файлов для быстрого поиска. Сильно грузит диск в первые дни после установки Windows, потом успокаивается."), L.T("Поиск по файлам станет медленнее."));
            Add("startmenuexperiencehost", L.T("Меню «Пуск»"), Verdict.Windows, L.T("Отображает меню «Пуск»."), L.T("Перезапустится при открытии «Пуска»."));
            Add("shellexperiencehost", L.T("Интерфейс Windows"), Verdict.Windows, L.T("Центр уведомлений, часы, панель быстрых настроек."), L.T("Перезапустится автоматически."));
            Add("textinputhost", L.T("Панель ввода"), Verdict.Windows, L.T("Эмодзи-панель (Win+.), сенсорная клавиатура, история буфера обмена."), L.T("Перезапустится при использовании."));
            Add("applicationframehost", L.T("Рамки окон приложений"), Verdict.Windows, L.T("Рисует рамки окон для приложений из Microsoft Store (Параметры, Калькулятор и др.)."), L.T("Закроются открытые такие приложения."));
            Add("systemsettings", L.T("Параметры Windows"), Verdict.Windows, L.T("Окно «Параметры»."), L.T("Просто закроется окно параметров."));
            Add("lockapp", L.T("Экран блокировки"), Verdict.Windows, L.T("Экран блокировки Windows."), L.T("Перезапустится при блокировке."));
            Add("smartscreen", "SmartScreen", Verdict.Windows, L.T("Проверяет репутацию скачанных программ и сайтов."), L.T("Защита от опасных загрузок временно перестанет работать."));
            Add("msmpeng|mpdefendercoreservice|nissrv|securityhealthservice|securityhealthsystray", L.T("Защитник Windows"), Verdict.System,
                L.T("Встроенный антивирус Windows. Нагружает систему при проверке файлов и установке программ — это нормально."), L.T("Закрыть нельзя, и не нужно: это ваша защита от вирусов."));
            Add("sppsvc", L.T("Защита программного обеспечения"), Verdict.Windows, L.T("Проверяет лицензию Windows."), L.T("Перезапустится сам."));
            Add("tiworker|trustedinstaller", L.T("Установщик обновлений Windows"), Verdict.Windows,
                L.T("Устанавливает обновления Windows. Может долго грузить процессор и диск после обновлений — лучше дождаться."), L.T("Прервётся установка обновлений — может привести к ошибкам."));
            Add("mousocoreworker|usoclient", L.T("Обновления Windows"), Verdict.Windows, L.T("Проверяет и скачивает обновления Windows."), L.T("Перезапустится при следующей проверке."));
            Add("compattelrunner", L.T("Телеметрия совместимости"), Verdict.Windows,
                L.T("Собирает сведения о совместимости программ для Microsoft. Иногда ненадолго сильно грузит диск и процессор."), L.T("Безвредно, но запустится снова по расписанию."));
            Add("msiexec", L.T("Установщик Windows"), Verdict.Windows, L.T("Устанавливает или удаляет программы в формате .msi."), L.T("Прервётся установка или удаление программы."));
            Add("rundll32", L.T("Запуск библиотек Windows"), Verdict.Caution,
                L.T("Системная утилита, которая запускает функции из DLL-файлов. Нормальна в папке System32, но ею часто пользуются вредоносы — если она висит постоянно и грузит систему, стоит проверить."), L.T("Закроется то, что она выполняла."));
            Add("mshta", L.T("Запуск HTML-приложений"), Verdict.Caution,
                L.T("Старый компонент Windows для HTML-приложений. Обычные программы почти не используют его, а вредоносы — часто. Если он работает и вы не знаете почему — проверьте компьютер антивирусом."), L.T("Закроется выполняемый сценарий."));
            Add("wscript|cscript", L.T("Сервер сценариев Windows"), Verdict.Caution,
                L.T("Выполняет сценарии .vbs и .js. Используется некоторыми программами, но и вредоносами тоже. Если работает постоянно без видимой причины — проверьте."), L.T("Прервётся выполняемый сценарий."));
            Add("powershell|pwsh", "PowerShell", Verdict.Caution,
                L.T("Командная оболочка Windows. Нормально, если вы или ваша программа её запустили. Если висит в фоне без причины — проверьте, что она делает."), L.T("Прервётся выполняемая команда или сценарий."));
            Add("cmd", L.T("Командная строка"), Verdict.App, L.T("Окно командной строки или скрипт, который её использует."), L.T("Прервётся выполняемая команда."));

            // ---------- Встроенные "помощники" Windows ----------
            Add("widgets|widgetservice|widgetboard", L.T("Виджеты Windows"), Verdict.Optional,
                L.T("Панель новостей, погоды и курсов слева на панели задач."), L.T("Можно отключить: Параметры → Персонализация → Панель задач → «Мини-приложения»."));
            Add("phoneexperiencehost|yourphone|phonelink", L.T("Связь с телефоном"), Verdict.Optional,
                L.T("Связывает компьютер с Android-телефоном или iPhone: уведомления, сообщения, фото."), L.T("Если не пользуетесь — Параметры → Bluetooth и устройства → Мобильные устройства: выкл."));
            Add("gamebar|gamebarftserver|gamebarpresencewriter", "Xbox Game Bar", Verdict.Optional,
                L.T("Игровая панель Windows (Win+G): запись экрана, счётчик FPS, чат Xbox."), L.T("Если не пользуетесь — Параметры → Игры → Game Bar: выкл."));
            Add("onedrive", "OneDrive", Verdict.Optional,
                L.T("Облачное хранилище Microsoft: синхронизирует файлы с интернетом."), L.T("Если не храните там файлы — закройте и уберите из автозагрузки. Если храните — оставьте, иначе файлы перестанут синхронизироваться."));
            Add("ms-teams|msteams|teams", "Microsoft Teams", Verdict.Optional, L.T("Рабочие звонки и чаты Microsoft."), L.T("Если не нужен каждый день — уберите из автозагрузки."));
            Add("copilot|m365copilot", "Copilot", Verdict.Optional, L.T("Помощник с ИИ от Microsoft."), L.T("Если не пользуетесь — уберите из автозагрузки."));
            Add("msedge", "Microsoft Edge", Verdict.App,
                L.T("Браузер. Может работать в фоне даже с закрытыми окнами (настройка «Продолжать работу фоновых расширений»)."), L.T("Закроются открытые вкладки Edge. Фоновую работу можно выключить: Настройки → Система и производительность."));
            Add("msedgewebview2", "Microsoft Edge WebView2", Verdict.Windows,
                L.T("Встроенный браузерный движок, на котором сделаны окна многих программ (Виджеты, Teams, часть Office и других). Сам по себе ничего не делает — его запускают другие программы."), L.T("Сломается окно программы, которая его использовала. Память освободится, только если закрыть саму эту программу."));

            // ---------- Драйверы и фирменный софт ----------
            Add("rtkauduservice64|rtkaudioservice64|ravbg64|ravcpl64", "Realtek Audio", Verdict.Windows,
                L.T("Служба звукового драйвера Realtek: эффекты звука, определение подключения наушников."), L.T("Могут перестать работать звуковые эффекты и автопереключение на наушники."));
            Add("dax3api", "Dolby Atmos / Dolby Audio", Verdict.Windows,
                L.T("Служба звуковых эффектов Dolby, которую ставят производители ноутбуков."), L.T("Пропадут эффекты Dolby, звук станет «как есть»."));
            Add("intelaudioservice", "Intel Audio", Verdict.Windows, L.T("Служба звукового драйвера Intel (Smart Sound Technology)."), L.T("Может пропасть звук или микрофон."));
            Add("igfxem|igfxhk|igfxtray|igfxcuiservice", L.T("Графика Intel"), Verdict.Windows, L.T("Служебные модули видеодрайвера Intel."), L.T("Перестанут работать горячие клавиши и настройки графики Intel."));
            Add("intelgraphicssoftware.service|intelgraphicssoftware|intelgraphicssoftware.service.graphics", "Intel Graphics Software", Verdict.Windows,
                L.T("Панель управления и служба видеодрайвера Intel."), L.T("Перестанет работать панель настроек графики Intel."));
            Add("esif_uf", "Intel Dynamic Tuning", Verdict.Windows, L.T("Управляет мощностью процессора и нагревом ноутбука."), L.T("Может хуже работать охлаждение и энергосбережение."));
            Add("nvcontainer|nvdisplay.container", L.T("NVIDIA (служба драйвера)"), Verdict.Windows, L.T("Служба видеодрайвера NVIDIA и панели управления."), L.T("Перестанут работать панель NVIDIA и часть функций драйвера."));
            Add("nvidia overlay|nvidia share", "NVIDIA Overlay", Verdict.Optional, L.T("Оверлей NVIDIA: запись игр, скриншоты, счётчик FPS."), L.T("Если не пользуетесь — выключите оверлей в приложении NVIDIA."));
            Add("radeonsoftware|amdrsserv|atieclxx|amdrssrcext", "AMD Radeon Software", Verdict.Windows, L.T("Панель и служба видеодрайвера AMD."), L.T("Перестанут работать панель AMD и её функции (запись, оверлей)."));
            Add("lenovovantageservice|lenovo.modern.imcontroller|imcontroller|lenovovantage", "Lenovo Vantage", Verdict.Optional,
                L.T("Фирменная программа Lenovo: обновления драйверов, режимы батареи и охлаждения."), L.T("Если режимы батареи настроены и обновления не нужны — можно не держать в фоне, но режимы питания могут сброситься."));

            // ---------- Популярные программы ----------
            Add("firefox", "Mozilla Firefox", Verdict.App, L.T("Браузер. Каждая вкладка и расширение — отдельные процессы, поэтому их много."), L.T("Закроются вкладки (Firefox предложит восстановить их при запуске)."));
            Add("chrome", "Google Chrome", Verdict.App, L.T("Браузер. Каждая вкладка и расширение — отдельные процессы."), L.T("Закроются вкладки. Может продолжать работать в фоне — это выключается в настройках «Система»."));
            Add("opera|opera_gx", "Opera", Verdict.App, L.T("Браузер."), L.T("Закроются вкладки."));
            Add("browser", L.T("Яндекс Браузер"), Verdict.App, L.T("Браузер Яндекса."), L.T("Закроются вкладки."));
            Add("vivaldi", "Vivaldi", Verdict.App, L.T("Браузер."), L.T("Закроются вкладки."));
            Add("brave", "Brave", Verdict.App, L.T("Браузер."), L.T("Закроются вкладки."));
            Add("discord", "Discord", Verdict.App, L.T("Голосовой и текстовый чат. Сделан на браузерном движке, поэтому несколько процессов."), L.T("Отключится голосовой чат."));
            Add("telegram", "Telegram", Verdict.App, L.T("Мессенджер."), L.T("Перестанут приходить уведомления, пока не откроете снова."));
            Add("whatsapp", "WhatsApp", Verdict.App, L.T("Мессенджер."), L.T("Перестанут приходить уведомления."));
            Add("viber", "Viber", Verdict.App, L.T("Мессенджер."), L.T("Перестанут приходить уведомления."));
            Add("zoom", "Zoom", Verdict.App, L.T("Видеозвонки."), L.T("Завершится звонок, если он идёт."));
            Add("skype", "Skype", Verdict.App, L.T("Звонки и чат."), L.T("Перестанут приходить звонки."));
            Add("spotify", "Spotify", Verdict.App, L.T("Музыка."), L.T("Остановится музыка."));
            Add("steam|steamwebhelper|steamservice", "Steam", Verdict.App, L.T("Магазин и лаунчер игр. steamwebhelper — его встроенный браузер (окна магазина, оверлей)."), L.T("Перестанут запускаться игры из Steam и обновления игр."));
            Add("epicgameslauncher|epicwebhelper", "Epic Games Launcher", Verdict.App, L.T("Магазин и лаунчер игр Epic."), L.T("Игры Epic не запустятся без него."));
            Add("anydesk", "AnyDesk", Verdict.App, L.T("Удалённый доступ к компьютеру."), L.T("К компьютеру нельзя будет подключиться удалённо. Если не ждёте подключений — можно закрывать."));
            Add("teamviewer|teamviewer_service", "TeamViewer", Verdict.App, L.T("Удалённый доступ к компьютеру."), L.T("К компьютеру нельзя будет подключиться удалённо."));
            Add("amneziavpn|amneziavpn-service", "AmneziaVPN", Verdict.App, L.T("VPN-клиент."), L.T("Отключится VPN."));
            Add("adobearm|adobeupdateservice", L.T("Обновления Adobe"), Verdict.Optional, L.T("Проверяет обновления Adobe Reader и других программ Adobe."), L.T("Обновления Adobe перестанут проверяться автоматически — сами программы работают."));
            Add("acrotray|adobecollabsync|ccxprocess|adobeipcbroker|coresync|creative cloud", L.T("Фоновые службы Adobe"), Verdict.Optional, L.T("Вспомогательные процессы Adobe Acrobat и Creative Cloud."), L.T("Программы Adobe продолжат работать, но синхронизация и часть функций — нет."));
            Add("googledrivefs", L.T("Google Диск"), Verdict.Optional, L.T("Синхронизация файлов с Google Диском."), L.T("Файлы перестанут синхронизироваться."));
            Add("dropbox", "Dropbox", Verdict.Optional, L.T("Синхронизация файлов с Dropbox."), L.T("Файлы перестанут синхронизироваться."));
            Add("memreduct|wisememoryoptimizer|cleanmem", L.T("«Очиститель» памяти"), Verdict.Optional,
                L.T("Принудительно выгружает данные программ из памяти. Цифра свободной памяти растёт, но программы потом подгружают всё обратно с диска и работают медленнее."), L.T("Лучше закрыть и убрать из автозагрузки."));
            Add("ollama|ollama app", "Ollama", Verdict.App, L.T("Запуск локальных нейросетей. Когда модель загружена, занимает много памяти."), L.T("Перестанут работать программы, которые к ней обращаются."));
            Add("code", "Visual Studio Code", Verdict.App, L.T("Редактор кода."), L.T("Закроется редактор — сохраните файлы."));
            Add("cursor", "Cursor", Verdict.App, L.T("Редактор кода с ИИ."), L.T("Закроется редактор — сохраните файлы."));
            Add("pycharm64|pycharm", "PyCharm", Verdict.App, L.T("Среда разработки на Python."), L.T("Закроется среда разработки — сохраните файлы."));
            Add("python|pythonw", "Python", Verdict.Caution,
                L.T("Запущенный скрипт на Python. Нормально, если вы его запускали или он нужен какой-то программе. pythonw работает без окна — если не знаете, что это за скрипт, стоит выяснить."), L.T("Остановится скрипт."));
            Add("node", "Node.js", Verdict.App, L.T("Запущенная программа на JavaScript (часто сервер разработки или часть другой программы)."), L.T("Остановится программа, которая её запустила."));
            Add("java|javaw", "Java", Verdict.App, L.T("Программа на Java (например, Minecraft или среда разработки)."), L.T("Закроется программа на Java."));
            Add("roomy", "Roomy", Verdict.App, L.T("Эта программа."), L.T("Roomy закроется."));
        }

        public static string VerdictTitle(Verdict v)
        {
            switch (v)
            {
                case Verdict.System: return L.T("Часть Windows — не трогать");
                case Verdict.Windows: return L.T("Компонент Windows — обычно нужен");
                case Verdict.Optional: return L.T("Можно отключить, если не пользуетесь");
                case Verdict.App: return L.T("Обычная программа");
                case Verdict.Caution: return L.T("Стоит присмотреться");
                default: return L.T("Нет в базе — смотрите факты о файле");
            }
        }
    }
}
