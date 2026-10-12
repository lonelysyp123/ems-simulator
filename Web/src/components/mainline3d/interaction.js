import * as THREE from 'three'
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js'

const CLICK_SLOP_PX = 6

/**
 * OrbitControls + Raycaster：单击断路器/设备面板，双击 BMS 进入设备详情
 */
export function createInteraction(camera, domElement, scene, { onBreakerClick, onDeviceClick, onDeviceDblClick, onPointerMove, onClick }) {
  const controls = new OrbitControls(camera, domElement)
  controls.enableDamping = true
  controls.dampingFactor = 0.08
  controls.rotateSpeed = 0.4
  controls.maxPolarAngle = Math.PI * 0.49
  controls.minDistance = 8
  controls.maxDistance = 800
  controls.screenSpacePanning = true
  controls.mouseButtons = {
    LEFT: THREE.MOUSE.ROTATE,
    MIDDLE: THREE.MOUSE.DOLLY,
    RIGHT: THREE.MOUSE.PAN
  }

  const raycaster = new THREE.Raycaster()
  const pointer = new THREE.Vector2()
  let down = null
  let suppressNextClick = false

  function ndcFromEvent(e) {
    const rect = domElement.getBoundingClientRect()
    pointer.x = ((e.clientX - rect.left) / rect.width) * 2 - 1
    pointer.y = -((e.clientY - rect.top) / rect.height) * 2 + 1
  }

  function pickHit(e) {
    ndcFromEvent(e)
    raycaster.setFromCamera(pointer, camera)
    return raycaster.intersectObjects(scene.children, true)
  }

  function onPointerDown(e) {
    if (e.button !== 0) return
    down = { x: e.clientX, y: e.clientY }
  }

  function onPointerUp(e) {
    if (e.button !== 0 || !down) return
    const dx = e.clientX - down.x
    const dy = e.clientY - down.y
    down = null
    if (Math.hypot(dx, dy) > CLICK_SLOP_PX) return
    if (suppressNextClick) {
      suppressNextClick = false
      return
    }

    onClick?.(e)

    const hits = pickHit(e)
    for (const hit of hits) {
      const id = hit.object?.userData?.pickId
      if (!id) continue
      const unitIndex = hit.object.userData.unitIndex
      onBreakerClick?.({
        pickId: id,
        unitIndex,
        layoutItem: hit.object.userData.layoutItem
      })
      return
    }

    for (const hit of hits) {
      const panelKey = hit.object?.userData?.panelKey
      if (!panelKey) continue
      onDeviceClick?.(panelKey)
      break
    }
  }

  function onDblClick(e) {
    e.preventDefault()
    suppressNextClick = true
    const hits = pickHit(e)
    for (const hit of hits) {
      const panelKey = hit.object?.userData?.panelKey
      if (!panelKey) continue
      onDeviceDblClick?.(panelKey)
      break
    }
  }

  function onMove(e) {
    onPointerMove?.(e)
  }

  function onLeave() {
    onPointerMove?.({ clientX: -1, clientY: -1, __leave: true })
  }

  function onContextMenu(e) {
    e.preventDefault()
  }

  domElement.addEventListener('pointerdown', onPointerDown)
  domElement.addEventListener('pointerup', onPointerUp)
  domElement.addEventListener('pointermove', onMove)
  domElement.addEventListener('pointerleave', onLeave)
  domElement.addEventListener('dblclick', onDblClick)
  domElement.addEventListener('contextmenu', onContextMenu)

  return {
    controls,
    dispose() {
      domElement.removeEventListener('pointerdown', onPointerDown)
      domElement.removeEventListener('pointerup', onPointerUp)
      domElement.removeEventListener('pointermove', onMove)
      domElement.removeEventListener('pointerleave', onLeave)
      domElement.removeEventListener('dblclick', onDblClick)
      domElement.removeEventListener('contextmenu', onContextMenu)
      controls.dispose()
    }
  }
}
