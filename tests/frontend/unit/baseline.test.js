import { describe, expect, it } from "vitest";

describe("baseline frontend harness", () => {
  it("uses synthetic fixtures", () => {
    expect({ classification: "SYNTHETIC" }).toEqual({ classification: "SYNTHETIC" });
  });
});
