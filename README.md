| Property                     | Value                                                   |
|------------------------------|---------------------------------------------------------|
| Track menu path              | BovineLabs → Timeline → Temporary Detach                |
| Track binding                | GameObject                                              |
| Clip type                    | Temporary Detach Clip                                   |
| Default clip duration        | 1 second                                                |
| Looping support              | Yes                                                     |
| Requires parent at start?    | Yes — silently skips if the bound object has no parent at clip start |
| World pose preserved while detached? | Only when the parent chain is uniformly scaled — detach writes `LocalTransform.FromMatrix(worldMatrix)` (uniform scale only), so non-uniform parent scale is lost |
| Restores on end?             | Reattaches to the original parent if it still exists and restores the captured local transform; if the original parent was destroyed, no reattach occurs |
| Track color                  | Teal                                                    |
