// World mode's scene graph — node-based, as opposed to Capsule's layer-based
// model. Nodes are NOT keyframed ("scripts for worlds, keyframes for
// capsules") — behavior comes from attached scripts (Assets, kind: 'script')
// reacting to events (hover/unhover/click) and mutating the node's transform
// live, both in the editor's NodeCanvas and in the exported HTML (see
// core/nodeRender.js, which both consume identically).
export interface SpritesheetConfig {
  assetId: string;
  cols: number;
  rows: number;
  frameW: number;
  frameH: number;
  fps: number;
}

export interface SceneNode {
  id: string;
  name: string;
  x: number;
  y: number;
  rotation: number;
  scale: number;
  // Referenced by id, never embedded — assets/scripts live in the Assets
  // docker and get attached here, matching "scripts get attached to nodes,
  // they are not immediately associated."
  assetIds: string[];
  scriptIds: string[];
  // A node with a spritesheet renders that instead of its plain assetIds
  // images (see core/nodeRender.js's buildNodeElement) — configured in the
  // dedicated Sprite Editor panel.
  spritesheet: SpritesheetConfig | null;
  children: SceneNode[];
}

export function newSceneNode(partial: Partial<SceneNode> = {}): SceneNode {
  return {
    id: `node_${crypto.randomUUID()}`,
    name: 'Node',
    x: 0,
    y: 0,
    rotation: 0,
    scale: 1,
    assetIds: [],
    scriptIds: [],
    spritesheet: null,
    children: [],
    ...partial,
  };
}
