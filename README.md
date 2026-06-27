# Full Logger



**GitHub:** [kabzon93region](https://github.com/kabzon93region)

**BepInEx-мод для Escape from Tarkov (SPT / Fika)** — универсальное полное логирование сессии для отладки модов и самой игры.



Работает на **любой** инстанции игры: клиент игрока, Fika headless host, listen-host или одиночный SPT. Один zip, один DLL — работает везде.



## Возможности



- Захват Unity Console, BepInEx логов, LogOutput.log, BSG Logs/

- Аудит Harmony-патчей с дампом всех установленных модов

- Снэпшот окружения: роль (PMC/Scav), Fika-режим, network

- Трассировка динамических методов (игра + все плагины)

- Ротация логов по **10 МБ** на часть

- Каждый запуск создаёт новую папку сессии



## Предупреждение



> ⚠️ **Только для отладки.** Большие логи, замедление старта (динамические Harmony-патчи), высокий расход диска. На headless патчи применяются медленнее (40/frame) чтобы избежать фризов.



## Установка



1. Скопировать FullLogger.dll в BepInEx/plugins/

2. Запустить игру

3. Логи появятся в BepInEx/FullLogger/



## Настройки (F12)



| Раздел | Параметр | По умолчанию | Описание |

|--------|----------|-------------|----------|

| General | Enabled | true | Включить/выключить логгер |

| General | Log Level | Info | Уровень логирования (Info/Debug/Trace) |

| Harmony | Dump Patches | true | Дамп всех Harmony-патчей при старте |

| Harmony | Method Traces | false | Трассировка вызовов методов |

| Output | Max File Size | 10 | Максимальный размер файла в МБ |

| Output | Session Folder | true | Создавать папку для каждой сессии |



## Известные проблемы



- Большой расход диска при включённом TRACE/HARMONY

- На headless допустим (метка universal), но для продакшена headless лучше не ставить — нагрузка



## Требования



- **SPT**: 4.0.x (протестировано на 4.0.13)

- **BepInEx**: 5.4.x

- **Fika**: совместим (проверен на Fika 2.3.x)



## Совместимость



- universal — работает на любой инстанции (клиент, headless host, headless client)



## Поддержать проект

Разовый донат картой РФ, СБП, ЮMoney, VK Pay:  

**[DonationAlerts → kabzon93region](https://www.donationalerts.com/r/kabzon93region)**
