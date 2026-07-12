heretek-dialogue-verb-talk = Поговорить
heretek-dialogue-conversation-examine = [color=lightblue]Сейчас этот персонаж разговаривает с { $count } { $count ->
    [one] человеком
    [few] людьми
   *[other] людьми
}.[/color]
heretek-dialogue-ui-raw-name = { $name }

heretek-dialogue-demo-001 = На этот раз проверяем не только сцену, но и выборы. Смотри на окно внимательно: после моей реплики под текстом появятся кнопки, и ничего не должно налезть друг на друга.
heretek-dialogue-demo-002 = Если это переживет длинные строки, музыку и еще кнопки выбора, значит каркас наконец становится похож на нормальную систему.
heretek-dialogue-demo-003 = С чего начнем проверку первого набора вариантов?
heretek-dialogue-demo-004 = Теперь окно должно сохранить тот же ритм и просто перейти дальше. Следующий набор уже на две кнопки, и они обязаны разойтись симметрично влево и вправо от центра.
heretek-dialogue-demo-004b = Сейчас проверим короткую сценку. После этой реплики окно исчезнет, NPC посмотрит влево, потом вправо, затем снова на игрока и только после смеха вернет диалог на экран.
heretek-dialogue-demo-005 = Выбирай, какой из двух вариантов тебе удобнее для проверки.
heretek-dialogue-demo-006 = Пока кнопки лежат отдельным рядом, тело текста не должно опускаться на них. Если под выборы заранее зарезервировано место, окно выглядит спокойно и не прыгает при раскрытии строки.
heretek-dialogue-demo-007 = Остался последний тест с одной кнопкой. Она должна стоять строго по центру. Готов?
heretek-dialogue-demo-008 = Если сейчас все выглядело ровно, значит выборы уже нормально встроены в сценовый UI, а не приклеены поверх него.
heretek-dialogue-demo-009 = Дальше можно без стыда переносить эту механику на полноценные сюжетные диалоги с ответами, мыслями и уже нормальными последствиями выбора.
heretek-dialogue-demo-hidden-001 = { " " }
heretek-dialogue-parallel-test-line = Тестовая реплика параллельного диалога.

heretek-dialogue-demo-choice-3a = Поставь левый вариант
heretek-dialogue-demo-choice-3a-selected = Давай сначала проверим левую кнопку.
heretek-dialogue-demo-choice-3a-response = Хорошо. Если эта кнопка выглядит смещенной или текст в ней упирается в края, значит центрирование все еще неправильное.

heretek-dialogue-demo-choice-3b = Оставь центр активным
heretek-dialogue-demo-choice-3b-selected = Тогда жму центральный вариант.
heretek-dialogue-demo-choice-3b-response = Именно он лучше всего показывает, сидит ли середина выбора ровно на оси окна.

heretek-dialogue-demo-choice-3c = Проверь правый край
heretek-dialogue-demo-choice-3c-selected = Хочу посмотреть, как ведет себя правый вариант.
heretek-dialogue-demo-choice-3c-response = Тоже верно. Правая кнопка обычно первой выдает перекос, если базовая точка выбрана не от центра.

heretek-dialogue-demo-choice-2a = Сравнить левую и правую
heretek-dialogue-demo-choice-2a-selected = Сначала сравню обе стороны между собой.
heretek-dialogue-demo-choice-2a-response = Это полезнее всего. Если одна сторона визуально тяжелее другой, проблема сразу станет заметна.

heretek-dialogue-demo-choice-2b = Проверить запас по ширине
heretek-dialogue-demo-choice-2b-selected = Лучше посмотрю, остается ли у кнопок нормальный запас по ширине.
heretek-dialogue-demo-choice-2b-response = Правильный подход. Даже при более длинной фразе текст не должен лезть ни в рамку кнопки, ни в тело реплики.

heretek-dialogue-demo-choice-1a = Да, завершаем проверку
heretek-dialogue-demo-choice-1a-selected = Да, запускай последний вариант.
heretek-dialogue-demo-choice-1a-response = Отлично. Одинокая кнопка по центру сразу показывает, не уехала ли базовая ось вправо или влево.
heretek-dialogue-demo-complete-chat = Первый тест завершен. Теперь на мне можно проверять уже состояние, счетчики и серверные последствия выбора.
heretek-dialogue-demo-loop-complete-chat = Диалог завершен. Если выбирал выдачу, счетчик уже должен был обновиться.
heretek-dialogue-demo-close-chat = Закрываю сцену сразу действием из варианта ответа. Если окно исчезло без лишней реплики, значит closeDialogue отработал правильно.

heretek-dialogue-state-001 = Визуальную часть ты уже видел. Теперь проверяем серверную логику: флаги, счетчики, выдачу предмета и переключение между разными диалогами на одном и том же NPC.
heretek-dialogue-state-002 = Если это работает стабильно, дальше уже можно переносить все в настоящую сцену, а не продолжать мучить одного и того же бедного манекена.
heretek-dialogue-state-003 = Выбирай, что именно проверяем на этом проходе.

heretek-dialogue-state-choice-1 = Выдать стакан виски
heretek-dialogue-state-choice-1-selected = Дай мне предмет прямо через действие диалога.
heretek-dialogue-state-choice-1-response = Держи. Когда закроешь эту реплику, в руку должен прийти стакан виски, а внутренний счетчик NPC обязан увеличиться на единицу.

heretek-dialogue-state-choice-2 = Ничего не выдавать
heretek-dialogue-state-choice-2-selected = На этот раз без награды, просто проверим обычное завершение.
heretek-dialogue-state-choice-2-response = Тоже полезно. Счетчик не должен измениться, но completeActions после конца диалога все равно обязаны отработать.

heretek-dialogue-state-choice-3 = Закрыть немедленно

heretek-dialogue-state-locked-001 = Лимит достигнут. Значит селектор диалога увидел счетчик, выбрал другой прототип и не пустил тебя в обычный reward-цикл.
heretek-dialogue-state-locked-chat = С тебя пока хватит. Лимит сработал без открытия сценового окна, значит chat-only ветка селектора отработала правильно.

heretek-dialogue-fizzo-intro-001 = Добро пожаловать в «Ale Wrath». Садись ближе к стойке и не косись так на копыта. Я ими наливаю аккуратнее, чем половина станции руками, а слушаю и вовсе лучше большинства священников.
heretek-dialogue-fizzo-intro-002 = Говорящая лошадь за стойкой. Космос всякий раз находит новый способ напомнить, что нормальность была лишь временной договоренностью.
heretek-dialogue-fizzo-intro-003 = Я одновременно хочу уставиться, извиниться и заказать что-нибудь покрепче. Пожалуй, начну с того, что меньше позорит.
heretek-dialogue-fizzo-intro-003b = Расслабься. В этом баре странности сначала получают табурет, а уже потом ярлык.
heretek-dialogue-fizzo-intro-004 = Как я оказалась за этой стойкой? Все просто. В один день станции понадобился не герой, а тот, кто умеет держать бокал ровнее, чем чужие нервы.
heretek-dialogue-fizzo-intro-005 = Она говорит лениво, почти шутя, но за этой легкостью слишком хорошо слышно, сколько чужих срывов и собственных дорог уже прошло мимо этой стойки.
heretek-dialogue-fizzo-intro-006 = Сначала я таскала ящики. Потом слушала чужие признания. Потом поняла, что бар на орбите это маленькое перемирие. Сюда приходят не потому, что счастливы, а потому что хотят хоть на минуту перестать держать спину прямо.
heretek-dialogue-fizzo-intro-007 = И тебя это правда устраивает?
heretek-dialogue-fizzo-intro-008 = Более чем. У стойки честнее всего видно, кто еще держится из упрямства, кто из гордости, а кто просто боится, что если сядет, то уже не поднимется.
heretek-dialogue-fizzo-intro-008b = Хороший бармен продает не бутылку. Он выдает человеку короткое разрешение выдохнуть и не лгать хотя бы самому себе.
heretek-dialogue-fizzo-intro-009 = Так что без церемоний. Для первого разговора у меня только один приличный финал, и он звенит стеклом.
heretek-dialogue-fizzo-intro-010 = Берешь стакан?
heretek-dialogue-fizzo-intro-011 = Держи. Пей медленно и не спорь с музыкой. Она здесь всегда первой понимает, кому нужен еще один глоток тишины.

heretek-dialogue-fizzo-intro-choice-1 = Говорящая лошадь?
heretek-dialogue-fizzo-intro-choice-1-selected = Прости, но я обязан спросить. Ты правда говорящая лошадь?
heretek-dialogue-fizzo-intro-choice-1-response = Формально да. Практически я бармен, а это звание здесь опаснее, чем любой диагноз.

heretek-dialogue-fizzo-intro-choice-2 = Промолчать
heretek-dialogue-fizzo-intro-choice-2-selected = Лучше пока просто послушаю.
heretek-dialogue-fizzo-intro-choice-2-response = И это, пожалуй, самая здравая реакция за сегодня. У молчунов обычно либо стальные нервы, либо очень длинная биография.

heretek-dialogue-fizzo-intro-choice-3 = Налей мне виски
heretek-dialogue-fizzo-intro-choice-3-selected = Налей мне виски.
heretek-dialogue-fizzo-intro-choice-3-response = Налью. Но сначала дам тебе историю покороче, чем похмелье после нее, иначе ты решишь, что я просто красиво тяну время.

heretek-dialogue-fizzo-intro-choice-4 = Возьму виски
heretek-dialogue-fizzo-intro-choice-4-selected = Ладно. От такого финала отказываться было бы уже невежливо.
heretek-dialogue-fizzo-intro-choice-4-response = Вот и правильно. Разговор должен уметь вовремя уступать место хорошему виски.
heretek-dialogue-fizzo-intro-complete-chat = Не стесняйся. Добавка у меня найдется, а лишние вопросы я беру только с тех, кто действительно хочет поговорить.

heretek-dialogue-fizzo-encore-001 = Снова ты. По глазам вижу: либо день оказался паршивее обычного, либо первый стакан сработал слишком честно.
heretek-dialogue-fizzo-encore-001b = Похоже, она замечает мое возвращение раньше, чем я сам успеваю решить, пришел за виски или за этой странной, почти уютной манерой разговаривать.
heretek-dialogue-fizzo-encore-002 = Еще один?
heretek-dialogue-fizzo-encore-003 = Стойка никуда не денется. Возвращайся, когда захочешь еще глоток, немного тишины или просто услышать, как кто-то не делает вид, будто все нормально.

heretek-dialogue-fizzo-encore-choice-1 = Да, давай еще
heretek-dialogue-fizzo-encore-choice-1-selected = Да. Налей еще один.
heretek-dialogue-fizzo-encore-choice-1-response = Ладно. Но если после этого начнешь спорить с табуреткой, я встану на сторону табуретки.

heretek-dialogue-fizzo-encore-choice-2 = Нет, мне хватит
heretek-dialogue-fizzo-encore-choice-2-selected = Нет, мне пока хватит.
heretek-dialogue-fizzo-encore-choice-2-response = Редкая дисциплина. Уважаю. Значит, сегодня ты еще делаешь вид, что управляешь своей жизнью.

heretek-dialogue-fizzo-cutoff-chat = С тебя уже хватит. Я наливаю виски, а не поднимаю посетителей из-под стойки.

heretek-dialogue-fizzo-base-001 = Снова у стойки. Что тебе нужно?
heretek-dialogue-fizzo-base-choice-store = Открыть лавку
heretek-dialogue-fizzo-base-choice-whiskey = Еще стаканчик
heretek-dialogue-fizzo-base-choice-whiskey-selected = Еще стаканчик.
heretek-dialogue-fizzo-base-choice-rumors = Спросить, что нового на станции
heretek-dialogue-fizzo-base-choice-leave = Ничего, я пойду

heretek-dialogue-fizzo-rumors-001 = У стойки новости не появляются — они сюда приползают, просят стакан воды и делают вид, что их никто не узнал.
heretek-dialogue-fizzo-rumors-002 = О чем спросить?
heretek-dialogue-fizzo-rumors-choice-1 = О пропавшем курьере
heretek-dialogue-fizzo-rumors-choice-1-selected = Говорят, у грузовых шлюзов пропал курьер. Ты что-нибудь слышала?
heretek-dialogue-fizzo-rumors-choice-1-response = Слышала, что он ушел не один и оставил на столе недопитый кофе. Для человека, который всегда допивал до дна, это уже почти крик о помощи. Если решишь искать, начни с грузового коридора и не верь первому, кто скажет, что ничего не видел.
heretek-dialogue-fizzo-rumors-choice-1-followup = Снова спросить о курьере
heretek-dialogue-fizzo-rumors-choice-1-followup-selected = О том пропавшем курьере ничего нового не слышно?
heretek-dialogue-fizzo-rumors-choice-1-followup-response = Ничего, что я назвала бы доказательством. Но архив камер в грузовом вычистили слишком аккуратно. Это не отсутствие новостей — это новости в перчатках. Ищи того, у кого был доступ к регистратору, прежде чем гоняться за тенями у шлюзов.
heretek-dialogue-fizzo-rumors-choice-2 = О самом тихом месте
heretek-dialogue-fizzo-rumors-choice-2-selected = Где на этой станции можно побыть в тишине?
heretek-dialogue-fizzo-rumors-choice-2-response = В тишине? Нигде. Но у старого обзорного окна шум хотя бы честный: только вентиляторы, звезды и твои собственные мысли. Иногда этого достаточно.
heretek-dialogue-fizzo-rumors-choice-3 = Неважно, я пойду
heretek-dialogue-fizzo-rumors-003 = Если услышишь что-то, что не хочется нести одному, возвращайся. У меня найдется табурет и ровно столько молчания, сколько нужно.
heretek-dialogue-ui-continue = >>>
