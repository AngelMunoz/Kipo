# Project Workflow

## 1. Development Process
- **Task-Driven Development:** All work must be organized into tracks and tasks.
- **Verification:** Every task or group of tasks must be verified before proceeding.
  - Priority: Automated unit tests for critical logic and important use cases.
  - Fallback: Manual verification for UI or complex system interactions.
- **Commits:**
  - Commits occur after a "fully verified unit of work."
  - **MANDATORY:** All commits require explicit user approval.
  - Summary location: **Git Notes**.

## 2. Testing Standards
- **Quality over Quantity:** Focus tests on core logic, edge cases, and high-value use cases rather than chasing a fixed coverage percentage.
- **Verification Protocol:**
  - 1. Run automated tests if available.
  - 2. Perform manual verification for visual or non-deterministic systems.
  - 3. Document the verification method in the task update.

## 3. Phase Completion Verification and Checkpointing Protocol
At the end of each phase:
1. **Consolidate:** Ensure all tasks in the phase are marked as completed.
2. **Global Verification:** Run full project build and all tests.
3. **Manual Review:** Request user to manually verify the phase's objectives.
4. **Checkpoint:** Create a git tag or note marking the phase completion.
5. **User Manual Verification Task:** This is the final meta-task for every phase: `- [ ] Task: Conductor - User Manual Verification '<Phase Name>' (Protocol in workflow.md)`.
