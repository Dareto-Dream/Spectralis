<script lang="ts">
  // Companion to the raw numeric hue field in SectionEditor — sections only
  // store a bare 0-360 hue (no saturation/lightness), so a real
  // <input type="color"> would need to invent S/L and silently lose the
  // round-trip. This is a fixed-S/L rainbow slider instead: visual picking,
  // exact value still comes from the number field next to it.
  let { value, onChange }: { value: number; onChange: (v: number) => void } = $props();
</script>

<input
  class="hueSlider"
  type="range"
  min="0"
  max="360"
  step="1"
  {value}
  oninput={(e) => onChange(+(e.target as HTMLInputElement).value)}
  style="--thumb-color: hsl({value}, 85%, 55%)"
/>

<style>
  .hueSlider {
    -webkit-appearance: none;
    appearance: none;
    width: 70px;
    height: 8px;
    border-radius: 4px;
    background: linear-gradient(
      to right,
      hsl(0, 85%, 55%),
      hsl(60, 85%, 55%),
      hsl(120, 85%, 55%),
      hsl(180, 85%, 55%),
      hsl(240, 85%, 55%),
      hsl(300, 85%, 55%),
      hsl(360, 85%, 55%)
    );
    border: 1px solid var(--line2);
  }
  .hueSlider::-webkit-slider-thumb {
    -webkit-appearance: none;
    width: 12px;
    height: 12px;
    border-radius: 50%;
    background: var(--thumb-color);
    border: 1px solid var(--text);
    cursor: pointer;
  }
  .hueSlider::-moz-range-thumb {
    width: 12px;
    height: 12px;
    border-radius: 50%;
    background: var(--thumb-color);
    border: 1px solid var(--text);
    cursor: pointer;
  }
</style>
