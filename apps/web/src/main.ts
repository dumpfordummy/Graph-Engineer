import { createApp } from 'vue'
import { createPinia } from 'pinia'
import { createRouter, createWebHistory } from 'vue-router'
import App from './App.vue'
import WorkflowList from './features/workflows/WorkflowList.vue'
import WorkflowEditor from './features/workflows/WorkflowEditor.vue'
import ProviderList from './features/providers/ProviderList.vue'
import ProviderEditor from './features/providers/ProviderEditor.vue'
import RunsView from './features/runs/RunsView.vue'
import '@vue-flow/core/dist/style.css'
import '@vue-flow/core/dist/theme-default.css'
import '@vue-flow/controls/dist/style.css'
import '@vue-flow/minimap/dist/style.css'
import './style.css'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', component: WorkflowList },
    { path: '/workflows/:id', component: WorkflowEditor },
    { path: '/settings/connections', component: ProviderList },
    { path: '/settings/connections/:id', component: ProviderEditor },
    { path: '/runs', component: RunsView },
    { path: '/runs/:id', component: RunsView },
    { path: '/:pathMatch(.*)*', redirect: '/' },
  ],
})
createApp(App).use(createPinia()).use(router).mount('#app')
