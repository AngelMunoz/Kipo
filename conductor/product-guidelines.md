# Product Guidelines

## Documentation Style
- **Developer-Centric yet Accessible:** Documentation should serve both core engineers and technical designers.
- **Technical & Precise:** Directly reference code structures (e.g., `Pomo.Core/Domain/AI.fs`), JSON properties, and math formulas where applicable.
- **Concise & Explanatory:** Explain the "how" and "why" without fluff. Use diagrams (Mermaid) and tables to break down complex relationships (e.g., Entity -> Archetype -> Model).
- **Practical Examples:** Always provide concrete JSON examples for configurations (Skills, AI, Particles).

## Code Style
- **Functional & Data-Oriented:** Strictly adhere to the functional paradigms outlined in `AGENTS.md` and `.agents/fsharp_conventions.md`.
- **Performance-Aware:** Prioritize low-allocation patterns (`structs`, `ValueOption`, `ArrayPool`) in critical paths.
- **Clear Separation:** Maintain strict separation between Data (Domain), Logic (Systems), and Configuration (JSON Content).

## Design & Communication
- **Iterative & Evolutionary:** Acknowledge that visual polish is secondary to architectural soundness.
- **Reference-Based:** Use existing documentation (`docs/`) as the standard for structure and depth.
- **Visuals:** Use Mermaid diagrams for flowcharts and architecture maps.
