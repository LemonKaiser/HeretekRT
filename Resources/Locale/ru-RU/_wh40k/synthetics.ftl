# WH40K Синтетики, Панель шасси и Шлем-HUD

ent-ActionWh40kChassisPanel = Диагностика шасси
    .desc = Открыть внутреннюю панель когитатора шасси: осмотр конечностей, компонентов, сервисный отсек, разгон и перезагрузка.

ent-WH40KOrganRadCollector = коллектор радиации
    .desc = Экранированный конденсаторный блок Механикус в торсе кибернетика. Поглощает до 300 рад; извлеките через отсек для стравливания или заварите для ремонта.

wh40k-synthetics-not-synthetic = У этого тела нет кибернетического шасси.

# UI Панели шасси
wh40k-chassis-ui-title = ДИАГНОСТИКА ШАССИ КОГИТАТОРА
wh40k-chassis-ui-tab-parts = КОНЕЧНОСТИ
wh40k-chassis-ui-tab-components = КОМПОНЕНТЫ
wh40k-chassis-ui-tab-augments = АУГМЕНТАЦИЯ
wh40k-chassis-ui-controls = УПРАВЛЕНИЕ ШАССИ
wh40k-chassis-ui-doll-hint = Клик по части
wh40k-chassis-ui-none = --
wh40k-chassis-ui-empty = [ПУСТО]
wh40k-chassis-ui-vital = ВАЖНО
wh40k-chassis-ui-confirm = ПОДТВЕРДИТЬ?

wh40k-chassis-ui-col-part = ЧАСТЬ
wh40k-chassis-ui-col-state = СТАТУС
wh40k-chassis-ui-col-integrity = ЦЕЛОСТНОСТЬ
wh40k-chassis-ui-col-faults = НЕИСПРАВНОСТИ
wh40k-chassis-ui-col-actions = ДЕЙСТВИЯ
wh40k-chassis-ui-col-slot = СЛОТ
wh40k-chassis-ui-col-component = КОМПОНЕНТ
wh40k-chassis-ui-col-health = ЗАРЯД / ПРОЧН.
wh40k-chassis-ui-col-band = СОСТОЯНИЕ

wh40k-chassis-ui-state-ok = НОРМА
wh40k-chassis-ui-state-isolated = ИЗОЛИР.
wh40k-chassis-ui-state-loose = ШАТАЕТСЯ
wh40k-chassis-ui-state-unpowered = ОБЕСТОЧ.
wh40k-chassis-ui-state-offline = ОТКАЗ
wh40k-chassis-ui-state-detached = ОТСОЕДИН.

wh40k-chassis-ui-isolate = ИЗОЛИР.
wh40k-chassis-ui-restore = ВКЛЮЧИТЬ
wh40k-chassis-ui-detach = СНЯТЬ
wh40k-chassis-ui-disconnect = ИЗВЛЕЧЬ
wh40k-chassis-ui-bay-open = ОТКРЫТЬ ОТСЕК
wh40k-chassis-ui-bay-close = ЗАКРЫТЬ ОТСЕК
wh40k-chassis-ui-reboot = ПЕРЕЗАГРУЗКА
wh40k-chassis-ui-overclock-on = РАЗГОН: ВКЛ
wh40k-chassis-ui-overclock-off = РАЗГОН: ВЫКЛ

wh40k-chassis-ui-row-power = ЗАРЯД
wh40k-chassis-ui-row-integrity = БРОНЯ
wh40k-chassis-ui-row-sensors = СЕНСОРЫ
wh40k-chassis-ui-row-servos = ПРИВОДЫ
wh40k-chassis-ui-row-core = ЯДРО
wh40k-chassis-ui-row-core-temp = {$value} K
wh40k-chassis-ui-row-heat = НАГРЕВ
wh40k-chassis-ui-row-faults = СБОИ
wh40k-chassis-ui-rad-collector = РАД-КОЛЛЕКТОР
wh40k-chassis-ui-rad-collector-none = НЕТ / СГОРЕЛ
wh40k-chassis-ui-radiation = ФОН РАДИАЦИИ: {$value} рад/с

wh40k-chassis-ui-bay-banner = ОТСЕК ШАССИ ОТКРЫТ — БРОНЯ ОГОЛЕНА
wh40k-chassis-ui-overclock-tag = РАЗГОН

wh40k-chassis-ui-tip-bay-open = Открыть сервисный отсек торса (3с / 6с себе). В поле можно устанавливать и извлекать батарею и радколлектор. Снижает скорость и оголяет броню.
wh40k-chassis-ui-tip-bay-close = Закрыть и зафиксировать бронеплиту сервисного отсека.
wh40k-chassis-ui-tip-reboot = Перезагрузка шасси (4с паралич, 10% заряда, КД 2 мин). Снимает стан, стамину, дрожь, слепоту и отключает разгон. Нажмите дважды для подтверждения.
wh40k-chassis-ui-tip-overclock = Разгон сервоприводов (+25% скорости). Тройной расход заряда, нагрев шасси, искры, риск КЗ и ожогов. Автовыключение при заряде <15% или перегреве.
wh40k-chassis-ui-tip-rad-collector = Внутренний накопитель радиации. Извлеките на операционном столе для стравливания; чините сваркой.
wh40k-chassis-ui-tip-radiation = Текущий уровень радиации, действующий на шасси.
wh40k-chassis-ui-tip-disconnect-closed = Сначала откройте сервисный отсек шасси.
wh40k-chassis-ui-tip-disconnect-table = Для извлечения этого модуля лягте на запитанный операционный стол аугментации.
wh40k-chassis-ui-tip-disconnect-vital = ЖИЗНЕННО ВАЖНЫЙ компонент! Нажмите дважды в течение 3 сек для подтверждения.
wh40k-chassis-ui-tip-requires-table = Снятие конечностей доступно только на запитанном операционном столе аугментации.

# Интеграция с операционным столом аугментации
wh40k-chassis-ui-dock-ready = ● ОПЕРАЦИОННЫЙ СТОЛ ПОДКЛЮЧЁН — ПОЛНЫЙ ДОСТУП К МОДУЛЯМ И АУГМЕНТАЦИИ
wh40k-chassis-ui-dock-offline = ▲ ОПЕРАЦИОННЫЙ СТОЛ ОБЕСТОЧЕН — ЗАМЕНА МОДУЛЕЙ ЗАБЛОКИРОВАНА
wh40k-chassis-ui-dock-field = ○ ПОЛЕВОЙ РЕЖИМ — ДОСТУПНА ТОЛЬКО ЗАМЕНА БАТАРЕИ И ПОЧИНКА
wh40k-chassis-ui-aug-banner-ready = СТОЛ АУГМЕНТАЦИИ ПОДКЛЮЧЁН // ВЫБЕРИТЕ ИМПЛАНТ ИЛИ ПРОТЕЗ ИЗ ИНВЕНТАРЯ ДЛЯ УСТАНОВКИ
wh40k-chassis-ui-aug-banner-offline = ОПЕРАЦИОННЫЙ СТОЛ ОБЕСТОЧЕН // ВОССТАНОВИТЕ ПИТАНИЕ ДЛЯ ЗАМЕНЫ МОДУЛЕЙ
wh40k-chassis-ui-aug-banner-field = ПОЛЕВОЙ РЕЖИМ // ЛЯГТЕ НА ЗАПИТАННЫЙ ОПЕРАЦИОННЫЙ СТОЛ ДЛЯ УСТАНОВКИ И ЗАМЕНЫ ИМПЛАНТОВ И ПРОТЕЗОВ
wh40k-chassis-ui-aug-available = ПОДХОДЯЩЕЕ ОБОРУДОВАНИЕ В ИНВЕНТАРЕ
wh40k-chassis-ui-aug-installed = УСТАНОВЛЕННЫЕ АУГМЕНТАЦИИ
wh40k-chassis-ui-aug-install = УСТАНОВИТЬ
wh40k-chassis-ui-aug-need-table = Лягте на запитанный операционный стол аугментации, чтобы просканировать инвентарь и установить импланты или протезы.
wh40k-chassis-ui-aug-none-installed = Установленных имплантов и протезов не обнаружено.
wh40k-chassis-ui-col-zone = ЗОНА
wh40k-chassis-ui-col-augment = МОДУЛЬ / ИМПЛАНТ
wh40k-chassis-ui-col-replaces = ЗАМЕНА
wh40k-chassis-table-open-panel = Открыть панель аугментации шасси
wh40k-chassis-table-linked = Когитатор шасси сопряжён с операционным столом аугментации.
wh40k-chassis-table-linked-unpowered = Операционный стол аугментации обесточен — доступ к замене модулей заблокирован.
wh40k-chassis-table-required-organ = Для извлечения этого модуля нужен запитанный операционный стол аугментации.
wh40k-chassis-table-required-install = Для этой установки нужен запитанный операционный стол аугментации.
wh40k-chassis-table-required-detach = Снятие конечностей доступно только на запитанном операционном столе аугментации.

# Статусы
wh40k-chassis-status-nominal = СТАТУС: В СЕТИ
wh40k-chassis-status-overclocked = СТАТУС: РАЗГОН
wh40k-chassis-status-bay-open = СТАТУС: ОТСЕК ОТКРЫТ
wh40k-chassis-status-low-power = СТАТУС: НИЗКИЙ ЗАРЯД
wh40k-chassis-status-reboot = СТАТУС: ПЕРЕЗАГРУЗКА
wh40k-chassis-status-critical = СТАТУС: КРИТИЧНО
wh40k-chassis-status-dead = СТАТУС: ОТКЛЮЧЕНО

# Слоты органов и состояния
wh40k-chassis-slot-brain = МОЗГ (ОРГАНИКА)
wh40k-chassis-slot-posbrain = ПОЗИТРОН. ЯДРО
wh40k-chassis-slot-eyes = ОПТИКА
wh40k-chassis-slot-leftEye = ЛЕВАЯ ОПТИКА
wh40k-chassis-slot-rightEye = ПРАВАЯ ОПТИКА
wh40k-chassis-slot-pump = НАСОС ОХЛАЖД.
wh40k-chassis-slot-radcollector = РАД-КОЛЛЕКТОР
wh40k-chassis-slot-heart = СЕРДЕЧНЫЙ МОДУЛЬ
wh40k-chassis-slot-lungs = ДЫХАТЕЛЬНЫЙ МОДУЛЬ
wh40k-chassis-slot-battery = БАТАРЕЯ
wh40k-chassis-slot-powerCell = ЯЧЕЙКА АУГМ.
wh40k-chassis-slot-armImplant = МОДУЛЬ РУКИ
wh40k-chassis-slot-handSmartlink = СИСТЕМА ПРИЦЕЛА
wh40k-chassis-slot-iatricMonitor = МЕД. МОНИТОР
wh40k-chassis-slot-armLaser = ЛАЗЕР В РУКЕ
wh40k-chassis-slot-handShield = ЩИТ В КИСТИ
wh40k-chassis-slot-shoulderCannon = ПЛЕЧЕВАЯ ПУШКА
wh40k-chassis-slot-haloBurst = ОСКОЛОЧНЫЙ ПОЯС
wh40k-chassis-slot-haloStun = КОНТУЗИОННЫЙ ПОЯС
wh40k-chassis-slot-opticalCamo = ОПТИЧ. МАСКИРОВКА
wh40k-chassis-slot-holoProjector = ГОЛОПРОЕКТОР
wh40k-chassis-slot-adrenalBooster = АДРЕНАЛ. СТИМУЛЯТОР
wh40k-chassis-slot-headReflexBooster = УСИЛИТЕЛЬ РЕФЛЕКСОВ
wh40k-chassis-slot-footSensor = ДАТЧИК СТОПЫ
wh40k-chassis-slot-liver = ПЕЧЕНЬ
wh40k-chassis-slot-nutrimentPump = ПИТАТЕЛЬНЫЙ НАСОС
wh40k-chassis-slot-handLighter = ВОСПЛАМЕНИТЕЛЬ
wh40k-chassis-slot-cardiacReviver = РЕАНИМАТОР СЕРДЦА

wh40k-chassis-band-charge = ЗАРЯД
wh40k-chassis-band-normal = НОРМА
wh40k-chassis-band-impaired = ИЗНОС
wh40k-chassis-band-failed = КРИТИЧНО

# Части тела
wh40k-chassis-part-head = ГОЛОВА
wh40k-chassis-part-torso = ТОРС
wh40k-chassis-part-left-arm = Л. РУКА
wh40k-chassis-part-right-arm = П. РУКА
wh40k-chassis-part-left-hand = Л. КИСТЬ
wh40k-chassis-part-right-hand = П. КИСТЬ
wh40k-chassis-part-left-leg = Л. НОГА
wh40k-chassis-part-right-leg = П. НОГА
wh40k-chassis-part-left-foot = Л. СТОПА
wh40k-chassis-part-right-foot = П. СТОПА
wh40k-chassis-part-chassis = ШАССИ

# Сообщения панели
wh40k-chassis-panel-refuse-dead = Когитатор шасси отключён.
wh40k-chassis-link-start-user = Подключение мультитула к диагностическому порту {THE($target)}...
wh40k-chassis-link-start-target = {CAPITALIZE(THE($user))} подключается к вашему диагностическому порту!
wh40k-chassis-link-done-target = {CAPITALIZE(THE($user))} подключился к вашей панели шасси.

# Отсек
wh40k-chassis-bay-no-torso = У этого шасси нет торсового отсека.
wh40k-chassis-bay-opening-self = Вы открываете свой сервисный отсек...
wh40k-chassis-bay-closing-self = Вы закрываете свой сервисный отсек...
wh40k-chassis-bay-opening = Вы открываете сервисный отсек {THE($target)}...
wh40k-chassis-bay-closing = Вы закрываете сервисный отсек {THE($target)}...
wh40k-chassis-bay-opening-other = {CAPITALIZE(THE($user))} открывает ваш сервисный отсек!
wh40k-chassis-bay-closing-other = {CAPITALIZE(THE($user))} закрывает ваш сервисный отсек.
wh40k-chassis-bay-opened = Сервисный отсек шасси со щелчком открылся.
wh40k-chassis-bay-closed = Сервисный отсек шасси защёлкнулся.
wh40k-chassis-bay-closed-refusal = Сначала откройте сервисный отсек шасси.
wh40k-chassis-bay-pulling = Извлечение: {THE($item)}...
wh40k-chassis-bay-pulling-other = {CAPITALIZE(THE($user))} извлекает ваш компонент: {$item}!
wh40k-chassis-bay-pulled = Компонент извлечён: {THE($item)}.
wh40k-chassis-bay-pulled-other = Из вашего шасси извлекли: {$item}!
wh40k-chassis-bay-installing = Установка {THE($item)} в сервисный отсек...
wh40k-chassis-bay-installed = Компонент установлен: {THE($item)}.
wh40k-chassis-bay-installed-other = В ваш сервисный отсек установили: {$item}.
wh40k-chassis-bay-no-slot = Нет подходящего слота для этого компонента.
wh40k-chassis-bay-slot-full = Этот слот уже занят.
wh40k-chassis-bay-verb-open = Открыть отсек шасси
wh40k-chassis-bay-verb-close = Закрыть отсек шасси
wh40k-chassis-bay-verb-pull = Извлечь {$item}
wh40k-chassis-bay-examine-open = [color=#ffb000]Сервисный отсек торса {THE($target)} открыт![/color]

# Изоляция и Лего-конечности
wh40k-chassis-isolate-refuse = Изолировать можно только конечности.
wh40k-chassis-isolate-refuse-cuffed = Нельзя изолировать руки в наручниках!
wh40k-chassis-isolate-done = Конечность изолирована: {$part}.
wh40k-chassis-restore-done = Конечность восстановлена: {$part}.
wh40k-chassis-restore-low-power = {CAPITALIZE($part)} включится после восстановления питания.
wh40k-chassis-detach-refuse = Эту часть нельзя отсоединить.
wh40k-chassis-detach-done = Отсоединено: {$part} ({THE($target)}).
wh40k-chassis-detach-done-owner = Ваша конечность отсоединилась: {$part}!

wh40k-synthetic-limb-not-chassis = К этому шасси можно прищёлкнуть только механическую или протезную конечность.
wh40k-synthetic-limb-no-socket = Нет свободного разъёма для этой конечности.
wh40k-synthetic-limb-reattaching = Прищёлкивание {THE($limb)}...
wh40k-synthetic-limb-reattaching-other = {CAPITALIZE(THE($user))} прищёлкивает {THE($limb)} к вашему шасси...
wh40k-synthetic-limb-reattached = {CAPITALIZE(THE($limb))} прищёлкнута (крепление разболтано).
wh40k-synthetic-limb-coupling-tightened = Крепление {$limb} подтянуто (осталось проходов кабелем: {$passes}).
wh40k-synthetic-limb-coupling-seated = Крепление {$limb} полностью зафиксировано!
wh40k-synthetic-limb-examine-loose = [color=#ffb000]Крепление {$part} у {THE($target)} разболтано (осталось проходов кабелем: {$passes}).[/color]

# Разгон и Перезагрузка
wh40k-chassis-reboot-refuse-dead = Шасси отключено и не может перезагрузиться.
wh40k-chassis-reboot-refuse-busy = Перезагрузка уже выполняется.
wh40k-chassis-reboot-refuse-cooldown = Перезагрузка будет доступна через {$seconds}с.
wh40k-chassis-reboot-start = {CAPITALIZE(THE($target))} запускает перезагрузку шасси!
wh40k-chassis-reboot-done = Перезагрузка шасси завершена. Системы стабилизированы.
wh40k-chassis-overclock-on = Разгон сервоприводов включён (+25% скорости).
wh40k-chassis-overclock-off = Разгон сервоприводов отключён.
wh40k-chassis-overclock-refuse-dead = Шасси отключено.
wh40k-chassis-overclock-refuse-low-power = Недостаточно энергии для разгона.
wh40k-chassis-overclock-refuse-charge = Заряд батареи ниже 15% — разгон невозможен.
wh40k-chassis-overclock-refuse-heat = Достигнут тепловой предел шасси!
wh40k-chassis-overclock-auto-off = Разгон отключён: {$reason}
wh40k-chassis-overclock-short = Искра в приводах! Разгон вызвал короткое замыкание!

# Низкий заряд
wh40k-low-power-enter = ЗАРЯД ИСЧЕРПАН — АВАРИЙНЫЙ РЕЖИМ (25% СКОРОСТИ, 1 РУКА)
wh40k-low-power-exit = Питание восстановлено. Полная мощность приводов.
wh40k-low-power-switch-cooldown = Переключение резервного питания ещё не завершено...
wh40k-low-power-switched = Резервное питание подано на: {$part}.
wh40k-low-power-verb-switch = Перебросить питание на {$part}

# Коллектор радиации
wh40k-rad-collector-saturated = Коллектор радиации переполнен! Излишек бьёт по шасси!
wh40k-rad-collector-burnt-out = Коллектор радиации сгорел и разрушен!
wh40k-rad-collector-missing = Коллектор радиации отсутствует! Радиация бьёт прямо по шасси!
wh40k-rad-collector-examine-charge = Накопленная радиация: [color=#ffb000]{$percent}%[/color]
wh40k-rad-collector-examine-integrity = Целостность конденсатора: [color=#7fdc6a]{$percent}%[/color]
wh40k-rad-collector-examine-venting = [color=#6fd6ff]Пассивно стравливает накопленную радиацию.[/color]
wh40k-rad-collector-weld-full = Коллектор радиации уже полностью исправен.
wh40k-rad-collector-welded = Коллектор радиации отремонтирован ({$percent}% целостности).

# Шлем-HUD
wh40k-hud-system = КОГИТАТОР :: СИСТЕМА
wh40k-hud-diagnostics = ДИАГНОСТИКА
wh40k-hud-row-rad = РАД-БЛОК
wh40k-hud-tag-info = ИНФО
wh40k-hud-tag-warn = ВНИМ
wh40k-hud-tag-crit = КРИТ
wh40k-hud-tag-fail = ОТКАЗ
wh40k-hud-advice = СОВЕТ: {$advice}
wh40k-hud-idle-1 = ТЕЛЕМЕТРИЯ ОМНИССИИ В НОРМЕ
wh40k-hud-idle-2 = ШИНА ПРИВОДОВ СИНХРОНИЗИРОВАНА
wh40k-hud-idle-3 = ТЕРМОРЕГУЛЯТОРЫ СТАБИЛЬНЫ
wh40k-hud-idle-4 = ЭНЕРГОСЕТЬ СБАЛАНСИРОВАНА
wh40k-hud-banner-standby = ОЖИДАНИЕ :: КРИТИЧЕСКИЙ СБОЙ ШАССИ
wh40k-hud-banner-rebooting = ПЕРЕЗАГРУЗКА ШАССИ...
wh40k-hud-banner-reboot-hint = ОТКРОЙТЕ ПАНЕЛЬ ШАССИ ИЛИ ЖДИТЕ ТЕХНОЖРЕЦА
wh40k-hud-banner-panic = ПАНИКА ЯДРА — ФАТАЛЬНЫЙ ОТКАЗ ШАССИ
wh40k-hud-panic-dump = 0x{$address} СБОЙ::СЕРВОШИНА_ОСТАНОВЛЕНА [СТЕК ПОВРЕЖДЁН]
wh40k-hud-death-banner = ШАССИ ОТКЛЮЧЕНО
wh40k-hud-death-sub = ДУХ МАШИНЫ ОСТАНОВЛЕН — ТРЕБУЕТСЯ РЕМОНТ И ЗАПУСК
wh40k-hud-banner-low-power = НИЗКИЙ ЗАРЯД — АВАРИЙНЫЙ ХОД
wh40k-hud-banner-overheat = ТЕПЛОВАЯ ТРЕВОГА — ПЕРЕГРЕВ
wh40k-hud-banner-integrity = КРИТИЧЕСКАЯ ЦЕЛОСТНОСТЬ ШАССИ ({$value})

# Строки неисправностей HUD
wh40k-hud-fault-detached = КОНЕЧНОСТЬ ОТСОЕД.
wh40k-hud-advice-detached = ПРИКРЕПИТЕ КОНЕЧНОСТЬ
wh40k-hud-fault-isolated = ПРИВОД ИЗОЛИРОВАН
wh40k-hud-advice-isolated = ВКЛЮЧИТЕ В ПАНЕЛИ
wh40k-hud-fault-loose = ШАТКОЕ КРЕПЛЕНИЕ
wh40k-hud-advice-loose = ЗАТЯНИТЕ КАБЕЛЕМ
wh40k-hud-fault-unpowered = РУКА ОБЕСТОЧЕНА
wh40k-hud-advice-unpowered = ЗАРЯДИТЕ БАТАРЕЮ
wh40k-hud-fault-offline = ПРИВОД ОТКАЗАЛ
wh40k-hud-advice-offline = ЗАВАРИТЕ ИЛИ ЗАМЕНИТЕ
wh40k-hud-fault-low-power = БАТАРЕЯ РАЗРЯЖЕНА
wh40k-hud-advice-low-power = ЗАРЯДИТЕСЬ ОТ ЛКП / ЗАМЕНА
wh40k-hud-fault-low-charge = МАЛЫЙ ЗАРЯД
wh40k-hud-advice-low-charge = ПОДЗАРЯДИТЕСЬ ОТ ЛКП
wh40k-hud-fault-overheat = ПЕРЕГРЕВ ПРИВОДОВ
wh40k-hud-advice-overheat = ОТКЛЮЧИТЕ РАЗГОН
wh40k-hud-fault-cold = ОБМЕРЗАНИЕ ШАССИ
wh40k-hud-advice-cold = НАЙДИТЕ ТЕПЛО / СКАФАНДР
wh40k-hud-fault-rad-saturated = РАД-КОЛЛЕКТОР ПОЛОН
wh40k-hud-advice-rad-saturated = ИЗВЛЕКИТЕ И СТРАВИТЕ
wh40k-hud-fault-rad-missing = НЕТ РАД-КОЛЛЕКТОРА
wh40k-hud-advice-rad-missing = УСТАНОВИТЕ РАД-КОЛЛЕКТОР
wh40k-hud-fault-brute = ПРОБОЙ БРОНИ
wh40k-hud-advice-brute = ЗАВАРИТЕ ОБШИВКУ
wh40k-hud-fault-heat = ТЕРМИЧЕСКИЙ ОЖОГ
wh40k-hud-advice-heat = ПОЧИНИТЕ КАБЕЛЕМ
wh40k-hud-fault-shock = ПРОБОЙ ЭЛЕКТРИКИ
wh40k-hud-advice-shock = ПОЧИНИТЕ КАБЕЛЕМ
wh40k-hud-fault-bay-open = ОТСЕК ОТКРЫТ
wh40k-hud-advice-bay-open = ЗАКРОЙТЕ ОТСЕК ШАССИ
