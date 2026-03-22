# Настройка FPS-персонажа (New Input System)

В папку `Assets/Scripts` добавлены:
- `PlayerMotor.cs` — движение, бег, прыжок, присед, слайд.
- `PlayerLook.cs` — поворот игрока мышью и вертикальный наклон камеры.

## 1) Подготовка сцены

Рекомендуемая иерархия:
- `Player` (пустой объект, это корень персонажа)
  - `PlayerBody` (Capsule)
  - `CameraPivot` (пустой объект на уровне глаз)
    - `Main Camera`

Что добавить на `Player`:
- `CharacterController`
- `PlayerMotor`
- `PlayerLook`

В `PlayerLook`:
- `Pitch Target` = `CameraPivot`.

## 2) Включить New Input System

1. Убедись, что пакет установлен:
   - `Window -> Package Manager -> Input System`.
2. Включи новую систему ввода:
   - `Edit -> Project Settings -> Player -> Active Input Handling`.
   - Выбери `Input System Package (New)` или `Both`.
3. Если Unity попросит перезапуск — перезапусти редактор.

## 3) Создать Input Actions

1. В Project создай asset: `Create -> Input Actions` (например `PlayerInputActions`).
2. Открой asset, создай Action Map: `Player`.
3. Создай Actions:
   - `Move` — `Value`, `Vector2`
   - `Look` — `Value`, `Vector2`
   - `Jump` — `Button`
   - `Sprint` — `Button`
   - `Crouch` — `Button`
4. Добавь бинды:
   - `Move`: 2D Vector Composite
     - Up = `W`
     - Down = `S`
     - Left = `A`
     - Right = `D`
   - `Look`: `Mouse/delta`
   - `Jump`: `Space`
   - `Sprint`: `Left Shift`
   - `Crouch`: `Left Ctrl`
5. Сохрани asset.

## 4) Привязать действия в скриптах

На компоненте `PlayerMotor` назначь:
- `Move Action` -> `Player/Move`
- `Jump Action` -> `Player/Jump`
- `Sprint Action` -> `Player/Sprint`
- `Crouch Action` -> `Player/Crouch`

На компоненте `PlayerLook` назначь:
- `Look Action` -> `Player/Look`

## 5) Базовые значения для соревновательного ощущения

`PlayerMotor`:
- `Walk Speed`: `4.8`
- `Sprint Speed`: `7.2`
- `Crouch Speed`: `3.0`
- `Ground Acceleration`: `28`
- `Ground Deceleration`: `34`
- `Air Acceleration`: `8`
- `Jump Height`: `1.2`
- `Gravity`: `24`

`PlayerLook`:
- `Sensitivity`: `0.11` (потом подстрой под себя)

## 6) Важно знать

- Скрипты рассчитаны на локальный прототип и комфортный старт для PvP-механики.
- Для сетевой версии позже нужно перейти на модель:
  - server authoritative,
  - client prediction,
  - reconciliation.
- Это позволит сохранить четкое управление и уменьшить влияние задержки.
