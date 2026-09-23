import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import NodeInspector from '../components/NodeInspector.vue'
import { useWorkflowStore } from '../store'
import { providerApi } from '../../providers/api'
import { profile } from '../../providers/__tests__/fixtures'

vi.mock('../../providers/api', () => ({ providerApi: { list: vi.fn() } }))
beforeEach(() => { setActivePinia(createPinia()); vi.mocked(providerApi.list).mockResolvedValue([profile(), profile({ id: 'profile-beta', name: 'Second provider' })]) })
describe('node inspector', () => {
  it('updates connection selectors when a canvas reconnect mutates the selected edge', async () => {
    const store = useWorkflowStore(); store.createNew()
    const edge = store.document!.definition.edges[1]!
    const id = store.addNode('modelCall')!
    store.selectEdge(edge.id)
    const wrapper = mount(NodeInspector)
    expect((wrapper.get('[aria-label="Target node"]').element as HTMLSelectElement).value).toBe(edge.targetNodeId)
    store.connect(edge.sourceNodeId, id, edge.id)
    await nextTick()
    expect((wrapper.get('[aria-label="Target node"]').element as HTMLSelectElement).value).toBe(id)
    wrapper.unmount()
  })
  it('edits prompt and descriptive metadata without changing the selected node identity', async () => {
    const store = useWorkflowStore(); store.createNew()
    const node = store.document!.definition.nodes[1]!
    store.selectNode(node.id)
    const wrapper = mount(NodeInspector)
    await wrapper.get('[aria-label="Node name"]').setValue('Review summary')
    await wrapper.get('[aria-label="Prompt"]').setValue('Check it carefully. <strong>This is text</strong>')
    await flushPromises()
    await wrapper.get('[aria-label="Provider profile"]').setValue('profile-alpha')
    expect(store.selectedNode?.name).toBe('Review summary')
    expect(store.selectedNode?.id).toBe(node.id)
    expect(store.selectedNode?.configuration).toEqual({ prompt: 'Check it carefully. <strong>This is text</strong>', providerProfileId: 'profile-alpha' })
    expect(wrapper.findAll('strong').some(item => item.text() === 'This is text')).toBe(false)
    expect(store.dirty).toBe(true)
    wrapper.unmount()
  })
  it('keeps unknown references unresolved and allows different profiles on two model nodes', async () => {
    const store = useWorkflowStore(); store.createNew()
    const first = store.document!.definition.nodes[1]!
    store.updateNode(first.id, node => { if (node.type === 'modelCall') node.configuration.providerProfileId = 'missing-import-reference' })
    store.selectNode(first.id)
    const wrapper = mount(NodeInspector, { global: { stubs: { RouterLink: true } } })
    await flushPromises()
    expect(wrapper.text()).toContain('Unresolved · missing-import-reference')
    expect(wrapper.text()).toContain('This draft can still be saved')
    await wrapper.get('[aria-label="Provider profile"]').setValue('profile-alpha')
    const secondId = store.addNode('modelCall')!
    await nextTick()
    await wrapper.get('[aria-label="Provider profile"]').setValue('profile-beta')
    const configs = store.document!.definition.nodes.filter(node => node.type === 'modelCall').map(node => node.configuration.providerProfileId)
    expect(configs).toEqual(['profile-alpha', 'profile-beta'])
    expect(store.selectedNode?.id).toBe(secondId)
    expect(wrapper.text()).toContain('Configured · not tested')
    wrapper.unmount()
  })
  it('shows each node type appropriate configuration and deletes its attached edges', async () => {
    const store = useWorkflowStore(); store.createNew()
    store.selectNode(store.document!.definition.nodes[0]!.id)
    const wrapper = mount(NodeInspector)
    expect(wrapper.find('[aria-label="Sample input"]').exists()).toBe(true)
    expect(wrapper.find('[aria-label="Prompt"]').exists()).toBe(false)
    const deleteButton = wrapper.findAll('button').find(button => button.text() === 'Delete node')!
    await deleteButton.trigger('click')
    expect(store.document!.definition.nodes).toHaveLength(2)
    expect(store.document!.definition.edges).toHaveLength(1)
    expect(wrapper.find('[aria-label="Workflow description"]').exists()).toBe(true)
    wrapper.unmount()
  })
})
