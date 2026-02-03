# DaVinCiCode

# DaVinCiCode

# Multiplayer Turn-Based Deduction Card Game (MVP)

A real-time multiplayer turn-based card deduction game built with Unity and Firebase Firestore.
This project focuses on strong game rule enforcement without a traditional server.

---

## 🎮 Features

- 3-player turn-based deduction gameplay
- Real-time multiplayer using Firebase Firestore
- State Machine–driven game flow
- Firestore Transaction–based authoritative logic
- Secure data access using Firestore Rules
- Full game loop: lobby → game → result → lobby

---

## 🧠 Core Design Principles

### 1. State Machine for Game Flow
Each phase of the game is represented as a separate state:
- Draw
- MustGuess
- GuessChoice
- Finished

This prevents invalid actions and simplifies turn logic.

---

### 2. Repository Pattern
All Firestore access is centralized in `GameRepository`.

- UI and controllers never access Firestore directly
- All game rules are executed inside Firestore transactions

---

### 3. Transaction-Based Rule Enforcement
Game logic is executed inside `RunTransactionAsync`:

- Prevents race conditions
- Blocks invalid or malicious client actions
- Ensures consistent game state without a server

---

### 4. GameContext as a Single Source of Truth
All real-time data from Firestore listeners is stored in `GameContext`.

- Simplifies rendering
- Decouples logic from network callbacks
- Makes debugging easier

---

## 🗂️ Firestore Data Structure

rooms/{roomId}
├─ players/{uid}
│ ├─ seat
│ ├─ ready
│ ├─ eliminated
│ └─ publicCards[]
├─ hands/{uid}
│ ├─ cardIds[]
│ └─ revealed[]
└─ game/state
├─ phase
├─ turnSeat
├─ turnUid
├─ deck
└─ winnerUid

---

## 🔐 Security (Firestore Rules)

- Only the current turn player can update game state
- Private hands are readable only by the owner and the turn player
- All rule enforcement is done server-side via Firestore Rules + Transactions

---

## 🧪 Solved Challenges

- Host eliminated mid-game
- Turn skipping when players are eliminated
- Game freeze after successful guess
- Persistent OUT state after match end
- Infinite scene loop after game finish

All issues were resolved through state validation and transaction-based checks.

---

## 🚀 Future Improvements

- Support for 4+ players
- Spectator mode
- Dedicated game server
- Match replay and analytics

---

## 🛠 Tech Stack

- Unity (C#)
- Firebase Firestore
- Firebase Authentication

---

## 📌 Status

This project is an MVP with a complete playable loop and solid architectural foundations.