## SuitTank
wh40k-suit-tank-switched = Скафандр автоматически переключился на запасной баллон.

## Warmth
wh40k-options-warmth-overlay = Температурная виньетка (мороз / жара)
wh40k-options-warmth-overlay-tooltip = Отображать эффект инея или перегрева по краям экрана при опасной температуре.

wh40k-warmth-hud-title = ТЕРМОАУСПЕКС
wh40k-warmth-here = Здесь { $celsius }°C
wh40k-warmth-here-vacuum = Здесь: вакуум
wh40k-warmth-near = Рядом тепло
wh40k-warmth-badge-damp = Влажно
wh40k-warmth-badge-soaked = Промок

wh40k-warmth-feel-freezing = Обморожение
wh40k-warmth-feel-cold = Холодно
wh40k-warmth-feel-chilly = Прохладно
wh40k-warmth-feel-comfortable = Комфортно
wh40k-warmth-feel-warm = Тепло
wh40k-warmth-feel-hot = Жарко
wh40k-warmth-feel-overheating = Перегрев

wh40k-warmth-cold-start = Вы опасно переохлаждаетесь. Найдите источник тепла, укрытие или тёплую одежду.
wh40k-warmth-heat-start = Температура тела слишком высокая. Охладитесь.
wh40k-warmth-cold-sign-1 = Вас бьёт неудержимая дрожь.
wh40k-warmth-cold-sign-2 = Пальцы онемели от холода.
wh40k-warmth-cold-sign-3 = Ваши зубы стучат от холода.
wh40k-warmth-heat-sign-1 = По лицу ручьями стекает пот.
wh40k-warmth-heat-sign-2 = От жары кружится голова.
wh40k-warmth-heat-sign-3 = Одежда прилипла к телу, насквозь пропитавшись потом.
wh40k-warmth-shiver-others = { CAPITALIZE($target) } сильно дрожит от холода.
wh40k-warmth-sweat-others = { CAPITALIZE($target) } обливается потом.

wh40k-warmth-examine-soaked = [color=lightblue]{ CAPITALIZE($target) } насквозь в воде.[/color]
wh40k-warmth-examine-damp = [color=lightblue]{ CAPITALIZE($target) } слегка в сырости.[/color]

wh40k-warmth-clothing-warm-to = [color=lightblue]Само по себе сохраняет тепло примерно до { $celsius }°C в сухом безветренном воздухе.[/color]
wh40k-warmth-clothing-deep-cold = [color=lightblue]Сохраняет тепло даже в экстремальном холоде.[/color]
wh40k-warmth-clothing-heat-shield = [color=orange]Защищает от жары.[/color]
wh40k-warmth-clothing-heat-trap = [color=orange]Удерживает тепло: в жарком воздухе вы перегреетесь быстрее.[/color]

## MarkingGlow
wh40k-marking-glow-toggle = Свечение
wh40k-marking-glow-tooltip = Отрисовывать эту особенность без затенения, чтобы она светилась в темноте.
wh40k-marking-glow-eyes = Светящиеся глаза
wh40k-marking-glow-eyes-tooltip = Отрисовывать глаза без затенения, чтобы они светились в темноте.
wh40k-marking-glow-hair = Светящиеся волосы
wh40k-marking-glow-facial-hair = Светящаяся борода
wh40k-marking-glow-hair-tooltip = Отрисовывать волосы без затенения, чтобы они светились в темноте.

## Construction repeat craft
wh40k-craft-repeat-count = x{ $count }
wh40k-craft-repeat-all = Все
wh40k-craft-repeat-tooltip = Изготовить { $count } раз подряд. Движение прервёт процесс.
wh40k-craft-repeat-all-tooltip = Изготавливать подряд, пока хватает материалов. Движение прервёт процесс.

## Lathe & Fabrication Silos
fabrication-silo-title = Фабрикационный силос
fabrication-silo-parts = Силос компонентов
fabrication-silo-chemicals = Химический силос
fabrication-silo-machines = Подключённые станки
fabrication-silo-components = Хранимые компоненты
fabrication-silo-reagents = Хранимые реагенты ({ NATURALFIXED($volume, 2) }/{ NATURALFIXED($max, 2) } ед.)
fabrication-silo-search = Поиск по запасам
fabrication-silo-parts-hint = Вставляйте компоненты, применяя их на силос, или высыпайте сразу из сумки; принимаются только детали, нужные станкам. Одинаковые компоненты группируются. Выберите станок ниже, чтобы подключить или отключить его.
fabrication-silo-chemicals-hint = Заливайте реагенты из мензурки, канистры или шприца; принимаются только вещества, нужные станкам. Внутри реагенты не вступают в реакцию и выдаются по одному: используйте Alt-клик с ёмкостью, чтобы вставить её и слить нужный реагент. Выберите станок ниже, чтобы подключить или отключить его.

fabrication-silo-part-stock = { $name } ×{ $count }
fabrication-silo-reagent-stock = { $reagent }: { NATURALFIXED($amount, 2) } ед.

fabrication-silo-part-rejected = Этот предмет не используется в рецептах фабрикации.
fabrication-silo-full = Силос компонентов заполнен.
fabrication-silo-insertion-failed = Силос не смог принять это содержимое.
fabrication-silo-dump-verb = Загрузить компоненты в { $silo }
fabrication-silo-parts-left = Предметы, не являющиеся деталями для фабрикации, остались в контейнере.
fabrication-silo-reagents-refused = Силос не принимает: { $reagents }.
fabrication-silo-fill-partial = Перелито { $amount } ед.; остальное не принято, так как силос не принимает: { $reagents }.

fabrication-silo-no-machines = Нет доступных станков. Станок должен быть на той же сетке, в пределах { $range } м и не подключён к другому силосу этого типа.
fabrication-silo-eject = Извлечь
fabrication-silo-eject-hint = Извлечь один предмет или стопку из этой группы.
fabrication-silo-empty = Подходящих запасов не найдено.

fabrication-silo-container = Ёмкость: { $name } ({ NATURALFIXED($volume, 2) }/{ NATURALFIXED($max, 2) } ед.)
fabrication-silo-no-container-inserted = Ёмкость не вставлена.
fabrication-silo-insert = Вставить
fabrication-silo-insert-hint = Вставить мензурку или сосуд из руки.
fabrication-silo-eject-container-hint = Извлечь ёмкость в руку.
fabrication-silo-withdraw-amount = { $amount } ед.
fabrication-silo-withdraw-all = Всё
fabrication-silo-withdraw-hint = Слить только этот реагент во вставленную ёмкость.
fabrication-silo-withdraw-blocked = Вставьте ёмкость со свободным местом для слива.
fabrication-silo-discard = Сбросить
fabrication-silo-discard-hint = Уничтожить весь запас этого реагента в силосе. Нажмите ещё раз для подтверждения.

fabrication-silo-no-container = Вставьте мензурку, чтобы слить реагент.
fabrication-silo-container-full = Ёмкость «{ $container }» заполнена.
fabrication-silo-no-draw = Хранимые реагенты можно извлекать только через интерфейс силоса.

lathe-menu-recipes-title = Чертежи
lathe-menu-quantity-hint = шт. в задании
lathe-menu-supplies-title = Подключённое снабжение

lathe-menu-queue-amount-tooltip = Запрошенное количество (до { $max }). Нажмите Enter или снимите фокус для применения.
lathe-menu-queue-empty = Очередь пуста. Выберите чертёж для начала работы.
lathe-menu-queue-index = { $index }
lathe-menu-queue-printed = { $printed }/
lathe-menu-queue-cancel = ×
lathe-menu-cancel-job = Отменить задание
lathe-menu-job-progress = Готово / заказано

lathe-menu-parts-silo-online = Силос компонентов подключён
lathe-menu-parts-silo-offline = Силос компонентов: не подключён или недоступен
lathe-menu-chemical-silo-online = Химический силос подключён
lathe-menu-chemical-silo-offline = Химический силос: не подключён или недоступен
lathe-menu-silo-link-hint = Откройте интерфейс силоса, чтобы подключить этот станок.

lathe-menu-status-printing = Печать
lathe-menu-status-ready = Готов
lathe-menu-status-waiting = Ожидание ресурсов
lathe-menu-status-design-unavailable = Чертёж недоступен

lathe-menu-queue-missing-line = • { $supply }
lathe-menu-queue-missing-material = { NATURALFIXED($amount, 2) } { $unit } — { $material }
lathe-menu-queue-missing-entity = { $amount } × { $material }
lathe-menu-queue-missing-reagent = { NATURALFIXED($amount, 2) } ед. { $material }

lathe-menu-recipe-ready = Готово к печати
lathe-menu-recipe-missing = В очередь (ожидает ресурсы)
lathe-menu-insert-parts-description = Вставьте необходимые компоненты, применив их на станок.

## ShipFacing
ui-options-function-wh40k-shuttle-face-cursor = Поворот корабля к курсору (удержание)

## RadarLabels
wh40k-options-general-radar = Ауспекс и радар
wh40k-radar-tags-more = ещё +{ $count }
wh40k-radar-tags-crowd = ×{ $count }
wh40k-radar-all-names-tooltip = Подписывать все контакты (детали под курсором)
ui-options-wh40k-radar-smart-tags = Умные метки радара: группировать наложения и показывать детали под курсором
ui-options-wh40k-radar-icons-only = Только иконки на радаре: скрывать названия до наведения курсора
ui-options-wh40k-radar-hover-reveal = Раскрывать названия контактов на радаре при наведении курсора

## Waypoints
wh40k-radar-cursor-coordinates = Курсор: { $x }, { $y }
wh40k-radar-cursor-unknown = Курсор: -
wh40k-radar-scanner-coordinates = Ауспекс: { $x }, { $y }
wh40k-radar-scanner-unknown = Ауспекс: -

wh40k-waypoint-header = ПУТЕВЫЕ ТОЧКИ
wh40k-waypoint-key = ТОЧКИ
wh40k-waypoint-key-tooltip = Путевые точки и координаты автопилота.
wh40k-waypoint-name = Название (необязательно)
wh40k-waypoint-x = X
wh40k-waypoint-y = Y
wh40k-waypoint-pick = Указать
wh40k-waypoint-pick-tooltip = Нажмите на радар, чтобы заполнить X и Y.
wh40k-waypoint-add = Добавить
wh40k-waypoint-add-tooltip = Сохранить координаты как общую путевую точку корабля.
wh40k-waypoint-fly = Курс
wh40k-waypoint-fly-tooltip = Направить автопилот в эту точку.
wh40k-waypoint-go = Курс
wh40k-waypoint-remove = X
wh40k-waypoint-remove-tooltip = Удалить путевую точку.
wh40k-waypoint-empty = Нет путевых точек.
wh40k-waypoint-position = X { $x } · Y { $y }
wh40k-waypoint-other-map = В другом секторе.
wh40k-waypoint-default-name = ТЧК-{ $id }
wh40k-waypoint-status-invalid = Введите корректные координаты X и Y.
wh40k-waypoint-status-full = Список путевых точек заполнен ({ $max }).
wh40k-waypoint-status-flying = Курс задан: X { $x } · Y { $y }

## ShipPreview
wh40k-ship-preview-title = Предпросмотр схемы судна
wh40k-ship-preview-no-vessel = Судно не выбрано
wh40k-ship-preview-loading = Загрузка схемы...
wh40k-ship-preview-failed = Не удалось загрузить схему судна
wh40k-ship-preview-info = { $width } × { $height } тайлов, заполнено { $count } тайлов
wh40k-ship-preview-planet-lift = Планетарный взлёт: до { $gravity } g (оценка)
wh40k-ship-preview-planet-none = Планетарный взлёт: маршевые двигатели не обнаружены
wh40k-ship-preview-rated-load = Грузоподъёмность: ~{ $tonnes } т до потери четверти тяги
wh40k-ship-preview-reset-view = Сбросить вид
wh40k-ship-preview-hint = ЛКМ/ПКМ — перемещение, колесо — масштаб

cmd-wh40k_shippreview-desc = Открывает клиентское окно предпросмотра для прототипа судна.
cmd-wh40k_shippreview-help = Использование: { $command } <id судна>
cmd-wh40k_shippreview-hint-vessel = <id судна>
cmd-wh40k_shippreview-unknown = Прототип судна с id «{ $id }» не найден.
cmd-wh40k_shippreview-session-required = Подключитесь к серверу, чтобы открыть предпросмотр судна.

## ShipArmor
wh40k-armor-backed-examine = Прикрыто корпусной бронёй, принимающей на себя [color=yellow]{ $share }%[/color] урона от взрывов, снарядов и лучей (включая бронебойные).
wh40k-armor-backed-examine-none = Не имеет корпусной брони за собой и получает полный урон.
wh40k-armor-backed-verb-text = Корпусная броня
wh40k-armor-backed-verb-message = Осмотреть степень прикрытия узла смежной корпусной бронёй.

## Ship Vox-PA
wh40k-shuttle-console-vox-tab = Вокс / Доступы
wh40k-shuttle-console-ship-alarms-label = Вокс и тревоги корабля
wh40k-shuttle-console-ship-condition = Код тревоги:
wh40k-shuttle-console-ship-general-quarters-sound = ОБЪЯВИТЬ БОЕВУЮ ТРЕВОГУ
wh40k-shuttle-console-ship-general-quarters-secure = ОТБОЙ БОЕВОЙ ТРЕВОГИ
wh40k-shuttle-console-ship-distress-send = ПОДАТЬ СИГНАЛ SOS
wh40k-shuttle-console-ship-distress-cancel = ОТМЕНИТЬ СИГНАЛ SOS
wh40k-shuttle-console-ship-distress-tooltip = Передать сигнал бедствия на общий канал и отметить корабль на всех радарах сектора.
wh40k-shuttle-console-ship-speakers = Громкоговорители
wh40k-shuttle-console-ship-speakers-fallback = Сигнализации воздуха (Вокс)
wh40k-shuttle-console-ship-speakers-count = { $online }/{ $total } в сети
wh40k-shuttle-console-ship-speakers-none = НЕТ РАБОЧИХ ДИНАМИКОВ
wh40k-shuttle-console-ship-volume = Громкость
wh40k-shuttle-console-ship-volume-value = { $percent }%
wh40k-shuttle-console-ship-announce-placeholder = Введите объявление для экипажа...
wh40k-shuttle-console-ship-announce = Передать

wh40k-ship-alert-code-green = Код Зелёный
wh40k-ship-alert-code-green-desc = Штатный режим. Угроз не обнаружено.
wh40k-ship-alert-code-green-announcement = Всему экипажу: код зелёный. Вернуться к штатному несению вахты.

wh40k-ship-alert-code-yellow = Код Жёлтый
wh40k-ship-alert-code-yellow-desc = Повышенная готовность. Ожидать приказов.
wh40k-ship-alert-code-yellow-announcement = Всему экипажу: код жёлтый. Закрепить снаряжение и занять дежурные посты.

wh40k-ship-alert-code-red = Код Красный
wh40k-ship-alert-code-red-desc = Боестолкновение. Боевая готовность.
wh40k-ship-alert-code-red-announcement = Всему экипажу: код красный. Занять боевые посты и задраить переборки. Это не учения.

wh40k-ship-alert-code-black = Код Чёрный
wh40k-ship-alert-code-black-desc = Неизбежная гибель судна. Эвакуация.
wh40k-ship-alert-code-black-announcement = Всему экипажу: покинуть корабль! Немедленно проследовать к спасательным капсулам и стыковочным шлюзам. Это не учения.

wh40k-ship-pa-general-quarters-announcement = Боевая тревога! Боевая тревога! Всему экипажу занять боевые посты!
wh40k-ship-pa-general-quarters-secure = Отбой боевой тревоги. Экипажу вернуться к штатной службе.

wh40k-ship-pa-sender = Вокс-сеть «{ $ship }»
wh40k-ship-pa-unknown-ship = Неизвестное судно
wh40k-ship-pa-announce-cooldown = Вокс-сеть ещё перезагружается. Подождите немного перед следующим объявлением.
wh40k-ship-pa-announce-no-speakers = На борту нет исправных динамиков вокс-сети для передачи.
wh40k-ship-pa-subtitle-title = { $ship } · ВОКС-СЕТЬ

wh40k-ship-pa-speaker-examine-ok = Индикатор горит ровным зелёным. Динамик готов передавать объявления.
wh40k-ship-pa-speaker-examine-damaged = Индикатор мигает янтарным, из решётки доносится тихий треск помех.
wh40k-ship-pa-speaker-examine-broken = Решётка разбита, динамик молчит. В таком состоянии сигнал не пройдёт.
wh40k-ship-pa-speaker-examine-unpowered = Индикатор не горит. Нет питания.

wh40k-ship-distress-radio = MAYDAY, MAYDAY, MAYDAY. Говорит «{ $ship }», запрашиваем немедленную помощь в координатах { $x }, { $y }.
wh40k-ship-distress-radio-cancel = Говорит «{ $ship }». Отбой сигнала бедствия.
wh40k-ship-distress-announcement = Сигнал бедствия передан. Корабль отмечен на всех радарах сектора.
wh40k-ship-distress-announcement-cancel = Сигнал бедствия отменён.
wh40k-ship-distress-announcement-lapsed = Срок действия сигнала бедствия истёк. Повторите вызов, если помощь всё ещё требуется.
wh40k-ship-distress-cooldown = Подождите { $seconds } сек. перед повторной отправкой сигнала бедствия.
wh40k-ship-distress-marker = SOS: { $ship }

ui-options-wh40k-ship-pa-volume = Корабельная вокс-сеть

cmd-wh40k_shippa_announce-desc = Передаёт объявление по вокс-сети корабля.
cmd-wh40k_shippa_announce-help = Использование: { $command } <сетка> <текст...>
cmd-wh40k_shippa_code-desc = Устанавливает код тревоги корабля.
cmd-wh40k_shippa_code-help = Использование: { $command } <сетка> <Wh40kShipCodeX>
cmd-wh40k_shippa_gq-desc = Включает или выключает боевую тревогу на корабле.
cmd-wh40k_shippa_gq-help = Использование: { $command } <сетка> <true|false>
cmd-wh40k_shippa-invalid-grid = «{ $grid }» не является сущностью сетки.
cmd-wh40k_shippa-invalid-code = «{ $code }» не является допустимым кодом тревоги корабля.
cmd-wh40k_shippa-no-speakers = На сетке нет исправных динамиков вокс-сети; ничего не было слышно.
cmd-wh40k_shippa-hint-code = <код тревоги>
cmd-wh40k_shippa-hint-active = <true|false>

## Entity translations (ru-RU)
ent-Wh40kMachinePartsSilo = силос компонентов
    .desc = Хранит компоненты-заготовки и подаёт их на подключённые фабрикаторы. Примените компонент на силос или высыпьте из сумки, чтобы загрузить детали для станков.

ent-Wh40kMachineChemicalSilo = химический силос
    .desc = Хранит до 1500 ед. реагентов без вступления в реакцию и подаёт их на подключённые фабрикаторы. Заливайте химикаты из мензурки, канистры или шприца; сливайте по одному во вставленную ёмкость.

ent-Wh40kPartsSiloMachineCircuitboard = плата силоса компонентов
    .desc = Печатная плата для сборки силоса компонентов.

ent-Wh40kChemicalSiloMachineCircuitboard = плата химического силоса
    .desc = Печатная плата для сборки химического силоса.

ent-Wh40kPartsSiloFlatpack = плоский пакет силоса компонентов
    .desc = Плоский пакет для быстрой сборки силоса компонентов.

ent-Wh40kChemicalSiloFlatpack = плоский пакет химического силоса
    .desc = Плоский пакет для быстрой сборки химического силоса.

ent-Wh40kWallmountShipPaSpeaker = корабельный вокс-динамик
    .desc = Настенный громкоговоритель корабельной вокс-сети. Передаёт объявления с мостика и сирены боевой тревоги, пока не разбит.
